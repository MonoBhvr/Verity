using System;
using System.Reflection;
var asm = Assembly.LoadFrom(@"C:\Users\dlgus\.nuget\packages\hexa.net.imgui.backends.sdl2\1.0.18\lib\net9.0\Hexa.NET.ImGui.Backends.SDL2.dll");
foreach (var t in asm.GetTypes())
{
    if (t.Name.Contains("SDL2") || t.Name.Contains("Viewport") || t.Name.Contains("Platform"))
    {
        Console.WriteLine(t.FullName);
        foreach (var m in t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly))
        {
            if (m.Name.Contains("Viewport") || m.Name.Contains("Window") || m.Name.Contains("Init") || m.Name.Contains("Style"))
                Console.WriteLine("  " + m);
        }
    }
}
