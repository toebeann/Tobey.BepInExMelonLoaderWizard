using HarmonyLib;
using System;

namespace Tobey.BepInExMelonLoaderWizard.ExtensionMethods;
internal static class TraverseExtensions
{
    public static Traverse OptionalMethod(this Traverse traverse, string name, params object[] arguments) =>
        TraverseHelper.SuppressHarmonyWarnings(() => traverse.Method(name, arguments));

    public static Traverse OptionalMethod(this Traverse traverse, string name, Type[] paramTypes, params object[] arguments) =>
        TraverseHelper.SuppressHarmonyWarnings(() => traverse.Method(name, paramTypes, arguments));
}
