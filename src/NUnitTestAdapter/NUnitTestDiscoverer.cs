﻿// ***********************************************************************
// Copyright (c) 2011 Charlie Poole
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

// #define LAUNCHDEBUGGER

using System.Collections.Generic;
#if LAUNCHDEBUGGER
using System.Diagnostics;
#endif
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using NUnit.Core;
using NUnit.Util;

namespace NUnit.VisualStudio.TestAdapter
{
    using System;

    [FileExtension(".dll")]
    [FileExtension(".exe")]
    [DefaultExecutorUri(NUnitTestExecutor.ExecutorUri)]
    public sealed class NUnitTestDiscoverer : NUnitTestAdapter, ITestDiscoverer
    {

        #region ITestDiscoverer Members

        public void DiscoverTests(IEnumerable<string> sources, IDiscoveryContext discoveryContext, IMessageLogger messageLogger, ITestCaseDiscoverySink discoverySink)
        {
            TestLog.Initialize(messageLogger);
            if (RegistryFailure)
            {
                TestLog.SendErrorMessage(ErrorMsg);
            }
            Info("discovering tests", "started");

            // Ensure any channels registered by other adapters are unregistered
            CleanUpRegisteredChannels();

            foreach (string sourceAssembly in sources)
            {
                TestLog.SendDebugMessage("Processing " + sourceAssembly);

                TestRunner runner = new TestDomain();
                var package = CreateTestPackage(sourceAssembly);
                TestConverter testConverter = null;
                try
                {
                    if (runner.Load(package))
                    {
                        testConverter = new TestConverter(TestLog, sourceAssembly);
                        int cases = ProcessTestCases(runner.Test, discoverySink, testConverter);
                        TestLog.SendDebugMessage(string.Format("Discovered {0} test cases", cases));
                    }
                    else
                    {
                        TestLog.NoNUnit2TestsFoundIn(sourceAssembly);
                    }
                }
                catch (BadImageFormatException)
                {
                    // we skip the native c++ binaries that we don't support.
                    TestLog.AssemblyNotSupportedWarning(sourceAssembly);
                }

                catch (FileNotFoundException ex)
                {
                    // Probably from the GetExportedTypes in NUnit.core, attempting to find an assembly, not a problem if it is not NUnit here
                    TestLog.DependentAssemblyNotFoundWarning(ex.FileName, sourceAssembly);
                }
                catch (FileLoadException ex)
                {
                    // Attempts to load an invalid assembly, or an assembly with missing dependencies
                    TestLog.LoadingAssemblyFailedWarning(ex.FileName, sourceAssembly);
                }
                catch (UnsupportedFrameworkException)
                {
                    TestLog.UnsupportedFrameworkWarning(sourceAssembly);
                }
                catch (Exception ex)
                {

                    TestLog.SendErrorMessage("Exception thrown discovering tests in " + sourceAssembly, ex);
                }
                finally
                {
                    runner.Unload();
                }
            }

            Info("discovering test", "finished");
        }

        private int ProcessTestCases(ITest test, ITestCaseDiscoverySink discoverySink, TestConverter testConverter)
        {
            int cases = 0;

            if (test.IsSuite)
            {
                cases += test.Tests.Cast<ITest>().Sum(child => ProcessTestCases(child, discoverySink, testConverter));
            }
            else
            {
                try
                {
#if LAUNCHDEBUGGER
            Debugger.Launch();
#endif
                    TestCase testCase = testConverter.ConvertTestCase(test);

                    discoverySink.SendTestCase(testCase);
                    cases += 1;
                }
                catch (Exception ex)
                {
                    TestLog.SendErrorMessage("Exception converting " + test.TestName.FullName, ex);
                }
            }

            return cases;
        }

        #endregion


    }
}
