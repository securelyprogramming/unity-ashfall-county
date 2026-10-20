using System;
using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;

/// <summary>World checks. Register each case with run("name", () => ...); assert with check(condition, "what").</summary>
static class WorldTests
{
    public static void All(Action<string, Action> run, Action<bool, string> check)
    {
    }
}
