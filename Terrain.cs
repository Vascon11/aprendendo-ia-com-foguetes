using System;

// Relevo do chao: uma altura que varia com o X do mundo (morros suaves).
// Fica PLANO perto do spawn (x = 0) pra decolar e pousar facil, e vira morros
// gradualmente conforme voce se afasta.
static class Terrain
{
    // World-Y do topo do terreno em x. 0 = plano; negativo = morro (pra cima).
    public static float TopY(float x)
    {
        float ax = MathF.Abs(x);
        float ramp = Math.Clamp((ax - 200f) / 350f, 0f, 1f); // 0 no spawn, 1 longe
        float hills = MathF.Sin(x * 0.0045f) * 34f
                    + MathF.Sin(x * 0.013f + 1.7f) * 16f
                    + MathF.Sin(x * 0.031f + 4.1f) * 7f;
        return -hills * ramp; // pra cima = Y negativo
    }

    // World-Y onde a BASE do foguete repousa (o centro fica 20 px acima do solo).
    public static float RestYAt(float x) => TopY(x) - 20f;
}
