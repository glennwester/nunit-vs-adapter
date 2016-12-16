﻿// ***********************************************************************
// Copyright (c) 2016 Charlie Poole
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace NUnit.VisualStudio.TestAdapter
{
    public class NavigationDataProvider
    {
        readonly string _assemblyPath;
        IDictionary<string, TypeDefinition> _typeDefs;

        public NavigationDataProvider(string assemblyPath)
        {
            _assemblyPath = assemblyPath;
        }

        public NavigationData GetNavigationData(string className, string methodName)
        {
            if (_typeDefs == null)
                _typeDefs = CacheTypes(_assemblyPath);

#if true
            TypeDefinition typeDef;
            if (!_typeDefs.TryGetValue(className, out typeDef))
                return NavigationData.Invalid;

            MethodDefinition methodDef = null;

            // Through NUnit 3.2.1, the class name provided by NUnit is
            // the reflected class - that is, the class of the fixture
            // that is being run. To use for location data, we actually
            // need the class where the method is defined.
            // TODO: Once NUnit is changed, try to simplify this.
            while (true)
            {
                methodDef = typeDef
                    .GetMethods()
                    .Where(o => o.Name == methodName)
                    .FirstOrDefault();

                if (methodDef != null)
                    break;

                var baseType = typeDef.BaseType;
                if (baseType == null || baseType.FullName == "System.Object")
                    return NavigationData.Invalid;

                typeDef = typeDef.BaseType.Resolve();
            }

            var sequencePoint = FirstOrDefaultSequencePoint(methodDef);
            if (sequencePoint != null)
                return new NavigationData(sequencePoint.Document.Url, sequencePoint.StartLine);

            return NavigationData.Invalid;
#else
            var navigationData = GetMethods(className)
                .Where(m => m.Name == methodName)
                .Select(FirstOrDefaultSequencePoint)
                .Where(x => x != null)
                .OrderBy(x => x.StartLine)
                .Select(x => new NavigationData(x.Document.Url, x.StartLine))
                .FirstOrDefault();

            return navigationData ?? NavigationData.Invalid;
#endif
        }

        static IDictionary<string, TypeDefinition> CacheTypes(string assemblyPath)
        {
            var readerParameters = new ReaderParameters() { ReadSymbols = true };
            var module = ModuleDefinition.ReadModule(assemblyPath, readerParameters);

            var types = new Dictionary<string, TypeDefinition>();

            foreach (var type in module.GetTypes())
                types[type.FullName] = type;

            return types;
        }

        IEnumerable<MethodDefinition> GetMethods(string className)
        {
            TypeDefinition type;

            if (_typeDefs.TryGetValue(StandardizeTypeName(className), out type))
                return type.GetMethods();

            return Enumerable.Empty<MethodDefinition>();
        }

        static SequencePoint FirstOrDefaultSequencePoint(MethodDefinition testMethod)
        {
            CustomAttribute asyncStateMachineAttribute;

            if (TryGetAsyncStateMachineAttribute(testMethod, out asyncStateMachineAttribute))
                testMethod = GetStateMachineMoveNextMethod(asyncStateMachineAttribute);

            return FirstOrDefaultUnhiddenSequencePoint(testMethod.Body);
        }

        static bool TryGetAsyncStateMachineAttribute(MethodDefinition method, out CustomAttribute attribute)
        {
            attribute = method.CustomAttributes.FirstOrDefault(c => c.AttributeType.Name == "AsyncStateMachineAttribute");
            return attribute != null;
        }

        static MethodDefinition GetStateMachineMoveNextMethod(CustomAttribute asyncStateMachineAttribute)
        {
            var stateMachineType = (TypeDefinition)asyncStateMachineAttribute.ConstructorArguments[0].Value;
            var stateMachineMoveNextMethod = stateMachineType.GetMethods().First(m => m.Name == "MoveNext");
            return stateMachineMoveNextMethod;
        }

        static SequencePoint FirstOrDefaultUnhiddenSequencePoint(MethodBody body)
        {
            const int lineNumberIndicatingHiddenLine = 16707566; //0xfeefee

            foreach (var instruction in body.Instructions)
                if (instruction.SequencePoint != null && instruction.SequencePoint.StartLine != lineNumberIndicatingHiddenLine)
                    return instruction.SequencePoint;

            return null;
        }

        static string StandardizeTypeName(string className)
        {
            //Mono.Cecil respects ECMA-335 for the FullName of a type, which can differ from Type.FullName.
            //In order to make reliable comparisons between the class part of a MethodGroup, the class part
            //must be standardized to the ECMA-335 format.
            //
            //ECMA-335 specifies "/" instead of "+" to indicate a nested type.

            return className.Replace("+", "/");
        }
    }
}
