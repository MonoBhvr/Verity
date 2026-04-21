using System;
using Lua;
class Program { 
    static void Main() { 
        var v = LuaValue.Nil;
        Console.WriteLine(v.Type.ToString());
    } 
}
