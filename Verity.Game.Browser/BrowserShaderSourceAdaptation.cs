namespace Verity.Game.Browser;

internal static class BrowserShaderSourceAdaptation
{
    public static string ToWebGl2Vertex(string source)
    {
        return RewriteVertexSource(source);
    }

    public static string ToWebGl2Fragment(string source)
    {
        return RewriteFragmentSource(source);
    }

    private static string RewriteVertexSource(string source)
    {
        string rewritten = NormalizeSource(source);
        rewritten = rewritten.Replace("#version 330 core", "#version 300 es\nprecision highp float;\nprecision highp int;");
        rewritten = rewritten.Replace("attribute ", "in ");
        rewritten = rewritten.Replace("varying ", "out ");
        rewritten = rewritten.Replace("texture2D(", "texture(");
        rewritten = rewritten.Replace(
            "uniform float uWeight[5] = float[] (0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216);",
            "const float uWeight[5] = float[](0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216);");
        rewritten = rewritten.Replace(
            "(1.0 / textureSize(uTexture, 0))",
            "(1.0 / vec2(textureSize(uTexture, 0)))");
        return rewritten;
    }

    private static string RewriteFragmentSource(string source)
    {
        string rewritten = NormalizeSource(source);
        rewritten = rewritten.Replace("#version 330 core", "#version 300 es\nprecision highp float;\nprecision highp int;");
        rewritten = rewritten.Replace("varying ", "in ");
        rewritten = rewritten.Replace("texture2D(", "texture(");
        if (rewritten.Contains("gl_FragColor", StringComparison.Ordinal) &&
            !rewritten.Contains("out vec4 FragColor;", StringComparison.Ordinal))
        {
            rewritten = rewritten.Replace("void main()", "out vec4 FragColor;\n\nvoid main()", StringComparison.Ordinal);
            rewritten = rewritten.Replace("gl_FragColor", "FragColor", StringComparison.Ordinal);
        }
        rewritten = rewritten.Replace(
            "uniform float uWeight[5] = float[] (0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216);",
            "const float uWeight[5] = float[](0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216);");
        rewritten = rewritten.Replace(
            "(1.0 / textureSize(uTexture, 0))",
            "(1.0 / vec2(textureSize(uTexture, 0)))");
        return rewritten;
    }

    private static string NormalizeSource(string source)
    {
        return source.Replace("\r\n", "\n").Trim();
    }
}
