﻿// ***********************************************************************
// Copyright (c) 2012 Charlie Poole
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// ***********************************************************************

using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using NUnit.Core;
using NUnit.Framework;
using NUnit.VisualStudio.TestAdapter.Tests.Fakes;

using VSTestResult = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResult;
using NUnitTestResult = NUnit.Core.TestResult;
using System.Runtime.Remoting;

namespace NUnit.VisualStudio.TestAdapter.Tests
{
    public class NUnitEventListenerTests
    {
        private static readonly string ThisAssemblyPath =
            Path.GetFullPath("NUnit.VisualStudio.TestAdapter.Tests.dll");
        private static readonly string ThisCodeFile =
            Path.GetFullPath(@"..\..\src\NUnitTestAdapterTests\NUnitEventListenerTests.cs");

        private const int LineNumber = 29; // Must be number of the following line
// ReSharper disable once UnusedMember.Local
        private void FakeTestMethod() { }

        private ITest fakeNUnitTest;
        private NUnitTestResult fakeNUnitResult;

        private NUnitEventListener listener;
        private FakeFrameworkHandle testLog;
        private TestConverter testConverter;

        [SetUp]
        public void SetUp()
        {
            MethodInfo fakeTestMethod = GetType().GetMethod("FakeTestMethod", BindingFlags.Instance | BindingFlags.NonPublic);
            fakeNUnitTest = new NUnitTestMethod(fakeTestMethod);

            fakeNUnitResult = new NUnitTestResult(fakeNUnitTest);
            fakeNUnitResult.SetResult(ResultState.Success, "It passed!", null);
            fakeNUnitResult.Time = 1.234;

            testLog = new FakeFrameworkHandle();

            testConverter = new TestConverter(new TestLogger(), ThisAssemblyPath);

            testConverter.ConvertTestCase(fakeNUnitTest);
            Assert.NotNull(testConverter.GetCachedTestCase(fakeNUnitTest.TestName.UniqueName));
            
            listener = new NUnitEventListener(testLog, testConverter);
        }

        #region TestStarted Tests

        [Test]
        public void TestStarted_CallsRecordStartCorrectly()
        {
            listener.TestStarted(fakeNUnitTest.TestName);
            Assert.That(testLog.Events.Count, Is.EqualTo(1));
            Assert.That(
                testLog.Events[0].EventType,
                Is.EqualTo(FakeFrameworkHandle.EventType.RecordStart));

            VerifyTestCase(testLog.Events[0].TestCase);
        }

        #endregion

        #region TestFinished Tests

        [Test]
        public void TestFinished_CallsRecordEnd_Then_RecordResult()
        {
            listener.TestFinished(fakeNUnitResult);
            Assert.AreEqual(2, testLog.Events.Count);
            Assert.AreEqual(
                FakeFrameworkHandle.EventType.RecordEnd,
                testLog.Events[0].EventType);
            Assert.AreEqual(
                FakeFrameworkHandle.EventType.RecordResult,
                testLog.Events[1].EventType);
        }

        [Test]
        public void TestFinished_CallsRecordEndCorrectly()
        {
            listener.TestFinished(fakeNUnitResult);
            Assume.That(testLog.Events.Count, Is.EqualTo(2));
            Assume.That(
                testLog.Events[0].EventType,
                Is.EqualTo(FakeFrameworkHandle.EventType.RecordEnd));

            VerifyTestCase(testLog.Events[0].TestCase);
            Assert.AreEqual(TestOutcome.Passed, testLog.Events[0].TestOutcome);
        }

        [Test]
        public void TestFinished_CallsRecordResultCorrectly()
        {
            listener.TestFinished(fakeNUnitResult);
            Assume.That(testLog.Events.Count, Is.EqualTo(2));
            Assume.That(
                testLog.Events[1].EventType,
                Is.EqualTo(FakeFrameworkHandle.EventType.RecordResult));
            
            VerifyTestResult(testLog.Events[1].TestResult);
        }

        [TestCase(ResultState.Success, TestOutcome.Passed, null)]
        [TestCase(ResultState.Failure, TestOutcome.Failed, "My failure message")]
        [TestCase(ResultState.Error, TestOutcome.Failed, "Error!")]
        [TestCase(ResultState.Cancelled, TestOutcome.None, null)]
        [TestCase(ResultState.Inconclusive, TestOutcome.None, null)]
        [TestCase(ResultState.NotRunnable, TestOutcome.Failed, "No constructor")]
        [TestCase(ResultState.Skipped, TestOutcome.Skipped, null)]
        [TestCase(ResultState.Ignored, TestOutcome.Skipped, "my reason")]
        public void TestFinished_OutcomesAreCorrectlyTranslated(ResultState resultState, TestOutcome outcome, string message)
        {
            fakeNUnitResult.SetResult(resultState, message, null);
            listener.TestFinished(fakeNUnitResult);
            Assume.That(testLog.Events.Count, Is.EqualTo(2));
            Assume.That(
                testLog.Events[0].EventType,
                Is.EqualTo(FakeFrameworkHandle.EventType.RecordEnd));
            Assume.That(
                testLog.Events[1].EventType,
                Is.EqualTo(FakeFrameworkHandle.EventType.RecordResult));

            Assert.AreEqual(outcome, testLog.Events[0].TestOutcome);
            Assert.AreEqual(outcome, testLog.Events[1].TestResult.Outcome);
            Assert.AreEqual(message, testLog.Events[1].TestResult.ErrorMessage);
        }

        #endregion

        #region TestOutput Tests

        [TestCaseSource("messageTestSource")]
        public void TestOutput_CallsSendMessageCorrectly(string nunitMessage, string expectedMessage)
        {
            listener.TestOutput(new TestOutput(nunitMessage, TestOutputType.Out));
            Assert.AreEqual(1, testLog.Events.Count);

            Assert.AreEqual(TestMessageLevel.Informational, testLog.Events[0].Message.Level);
            Assert.AreEqual(expectedMessage, testLog.Events[0].Message.Text);
        }

        private static readonly string Nl = Environment.NewLine;
        private const string Message = "MESSAGE";
        private const string Line1 = "LINE#1";
        private const string Line2 = "\tLINE#2";

        private readonly TestCaseData[] messageTestSource = 
        {
            new TestCaseData(Message, Message),
            new TestCaseData(Message + Nl, Message),
            new TestCaseData(Message + "\r\n", Message),
            new TestCaseData(Message + "\n", Message),
            new TestCaseData(Message + "\r", Message),
            new TestCaseData(Line1 + Nl + Line2, Line1 + Nl + Line2),
            new TestCaseData(Line1 +"\r\n" + Line2, Line1 +"\r\n" + Line2),
            new TestCaseData(Line1 +"\n" + Line2, Line1 + "\n" + Line2),
            new TestCaseData(Line1 +"\r" + Line2, Line1 + "\r" + Line2),
            new TestCaseData(Line1 +"\r\n" + Line2 + "\r\n", Line1 +"\r\n" + Line2),
            new TestCaseData(Message + Nl + Nl, Message + Nl),
            new TestCaseData(Message + "\r\n\r\n", Message + "\r\n"),
            new TestCaseData(Message + "\n\n", Message + "\n"),
            new TestCaseData(Message + "\r\r", Message + "\r")
        };

        #endregion

        #region Listener Lifetime Tests
        [Test]
        public void Listener_LeaseLifetimeWillNotExpire()
        {
            testLog = new FakeFrameworkHandle();
            testConverter = new TestConverter(new TestLogger(), ThisAssemblyPath);
            MarshalByRefObject localInstance = (MarshalByRefObject)Activator.CreateInstance(typeof(NUnitEventListener), testLog, testConverter);

            RemotingServices.Marshal(localInstance);

            var lifetime = ((MarshalByRefObject)localInstance).GetLifetimeService();
            
            // A null lifetime (as opposed to an ILease) means the object has an infinite lifetime
            Assert.IsNull(lifetime);
        }
        #endregion

        #region Helper Methods

        private void VerifyTestCase(TestCase ourCase)
        {
            Assert.NotNull(ourCase, "TestCase not set");
            Assert.That(ourCase.DisplayName, Is.EqualTo(fakeNUnitTest.TestName.Name));
            Assert.That(ourCase.FullyQualifiedName, Is.EqualTo(fakeNUnitTest.TestName.FullName));
            Assert.That(ourCase.Source, Is.EqualTo(ThisAssemblyPath));
            Assert.That(ourCase.CodeFilePath, Is.SamePath(ThisCodeFile));
            Assert.That(ourCase.LineNumber, Is.EqualTo(LineNumber));
        }

        private void VerifyTestResult(VSTestResult ourResult)
        {
            Assert.NotNull(ourResult, "TestResult not set");
            VerifyTestCase(ourResult.TestCase);

            Assert.AreEqual(Environment.MachineName, ourResult.ComputerName);
            Assert.AreEqual(TestOutcome.Passed, ourResult.Outcome);
            Assert.AreEqual("It passed!", ourResult.ErrorMessage);
            Assert.AreEqual(TimeSpan.FromSeconds(1.234), ourResult.Duration);
        }

        #endregion
    }
}
