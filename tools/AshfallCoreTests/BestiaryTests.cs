using System;
using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;

/// <summary>Bestiary checks. Register each case with run("name", () => ...); assert with check(condition, "what").</summary>
static class BestiaryTests
{
    public static void All(Action<string, Action> run, Action<bool, string> check)
    {
    }
}
