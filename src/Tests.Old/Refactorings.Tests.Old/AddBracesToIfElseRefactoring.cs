﻿// Copyright (c) Josef Pihrt and Contributors. Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Roslynator.CSharp.Refactorings.Tests
{
    internal class AddBracesToIfElseRefactoring
    {
        public bool SomeMethod()
        {
            string value = GetValue();

            if (value.StartsWith("a"))
                return true;
            else if (value.StartsWith("b"))
                return true;

            return false;
        }










        private string GetValue()
        {
            return null;
        }
    }
}