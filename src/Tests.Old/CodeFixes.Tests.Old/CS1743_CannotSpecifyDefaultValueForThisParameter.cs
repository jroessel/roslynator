﻿// Copyright (c) Josef Pihrt and Contributors. Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#pragma warning disable RCS1213

namespace Roslynator.CSharp.CodeFixes.Tests
{
    internal static class CS1743_CannotSpecifyDefaultValueForThisParameter
    {
        private static void Foo(this object value = null)
        {
        }
    }
}