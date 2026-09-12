using System;

// Constantes de fisica e da IA, num lugar so (compartilhadas por humano e IA).
static class Config
{
    // --- Fisica ---
    public const float Gravity      = 220f;   // px/s^2
    public const float MaxThrust    = 2600f;  // forca maxima do motor
    public const float DryMass      = 1.0f;   // massa sem combustivel
    public const float FuelMass     = 0.05f;  // massa por unidade de combustivel
    public const float StartFuel    = 100f;   // combustivel inicial
    public const float BurnRate     = 12f;    // combustivel/s no throttle cheio
    public const float ThrottleRate = 1.2f;   // ajuste de throttle por segundo (humano)
    public const float PixelsPerM   = 10f;    // 10 px = 1 metro
    public static float MaxLandingSpeed = 8f; // m/s: acima disso explode ao tocar o chao

    // --- Rotacao / estabilidade ---
    public const float TorquePower  = 340f;   // deg/s^2 aplicados por A/D (ou pela IA)
    public const float AngularDamp  = 0.6f;   // amortecimento do giro
    public const float InstabFactor = 0.14f;  // desestabilizacao aerodinamica
    public const float ThrustNoise  = 10f;    // tremor do giro com o motor ligado

    public const float GroundY   = 0f;
    public const float RestY     = GroundY - 20f; // base do foguete toca o chao
    public const float LaunchGap = 25f;           // subir isso = "decolou"

    public static readonly float Deg2Rad = MathF.PI / 180f;
    public static readonly float Rad2Deg = 180f / MathF.PI;

    // --- Algoritmo genetico ---
    public const int   PopSize        = 60;   // foguetes por geracao
    public const int   Elites         = 6;    // melhores que passam intactos
    public const int   Immigrants     = 5;    // foguetes aleatorios novos por geracao (diversidade)
    public const int   RerollStuckGens = 100; // se travar tantas geracoes SEM nunca pousar, recomeca do zero
    public const int   TournamentK    = 4;    // candidatos por torneio de selecao
    public const float MutationRate   = 0.12f;// chance de cada peso sofrer mutacao
    public const float MutationStdDev = 0.4f; // intensidade da mutacao
    public static float MaxEpisodeTime = 45f; // segundos de simulacao por geracao
    public static float HeightReward  = 6f;   // pontos por metro de altura ao pousar (empurra a subir)
}
