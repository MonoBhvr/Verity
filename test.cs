using System;
using Lua;
class Program { static void Main() { Console.WriteLine(typeof(LuaValueType).ToString()); foreach(var name in Enum.GetNames(typeof(LuaValueType))) Console.WriteLine(name); } }
