using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;

// ---------------------------------------------------------------------------
//  FOGUETE + IA (neuroevolucao)
//
//  Modos (troque com as teclas 1/2/3):
//    1  MANUAL      -> voce pilota (A/D gira, W/S forca)
//    2  TREINO IA   -> assiste o algoritmo genetico evoluir a populacao
//    3  ASSISTIR    -> ve o melhor cerebro ja evoluido voar sozinho
//
//  Gerais:  F tela cheia   R reinicia   ESC sair
//  No treino:  seta CIMA/BAIXO muda a velocidade da simulacao
//
//  Veja DOCUMENTACAO.md pra entender como a IA funciona.
// ---------------------------------------------------------------------------

const int startW = 1600;
const int startH = 900;

// Auto-teste headless: "dotnet run -- selftest [geracoes]" evolui sem janela
// e imprime o fitness, so pra confirmar que a IA realmente aprende.
if (args.Length > 0 && args[0] == "selftest")
{
    int gens = args.Length > 1 ? int.Parse(args[1]) : 40;
    int seed = args.Length > 2 ? int.Parse(args[2]) : 7;
    if (args.Length > 3) Config.HeightReward = float.Parse(args[3]);
    if (args.Length > 4) Config.MaxEpisodeTime = float.Parse(args[4]);
    if (args.Length > 5) Config.MaxLandingSpeed = float.Parse(args[5]);
    var testRng = new Random(seed);
    var testPop = new Population(testRng);
    for (int g = 0; g < gens; g++)
    {
        while (!testPop.AllDone()) testPop.Step(1f / 60f);
        testPop.Evolve();
        if (g % 5 == 0 || g == gens - 1)
            Console.WriteLine($"Ger {testPop.Generation - 1,4} [{(testPop.LandingPhase ? "POUSO" : "VOO  ")}]: " +
                              $"melhor={testPop.BestFitness,7:0}  " +
                              $"melhor_pouso={testPop.BestLandedAltEver,6:0.0}m  pousos={testPop.LandedCount}");
    }
    // verifica salvar/carregar (round-trip)
    testPop.Save("selftest_save.dat");
    var p2 = new Population(new Random(1));
    bool ok = p2.Load("selftest_save.dat");
    bool genOk = p2.Generation == testPop.Generation;
    bool weightOk = p2.Brains[0].W[0] == testPop.Brains[0].W[0]
                 && p2.Brains[Config.PopSize - 1].W[NeuralNet.GenomeSize - 1]
                    == testPop.Brains[Config.PopSize - 1].W[NeuralNet.GenomeSize - 1];
    Console.WriteLine($"Save/Load: ok={ok} geracao_confere={genOk} pesos_conferem={weightOk} " +
                      $"(gen {p2.Generation}, melhor_pouso {p2.BestLandedAltEver:0.0}m)");
    System.IO.File.Delete("selftest_save.dat");

    Console.WriteLine("Auto-teste concluido.");
    return;
}

SetConfigFlags(ConfigFlags.ResizableWindow);
InitWindow(startW, startH, "Foguete + IA");
SetTargetFPS(60);

var rng = new Random();

// campo de estrelas (tile do tamanho do monitor)
int mon = GetCurrentMonitor();
int tileW = Math.Max(1600, GetMonitorWidth(mon));
int tileH = Math.Max(900, GetMonitorHeight(mon));
var starRng = new Random(1234);
var stars = new (float X, float Y, byte B)[520];
for (int i = 0; i < stars.Length; i++)
    stars[i] = (starRng.Next(0, tileW), starRng.Next(0, tileH), (byte)starRng.Next(120, 255));

// --- estado dos modos ---
Mode mode = Mode.Manual;

// modo manual
var player = new Rocket(rng);
var particles = new List<Particle>();
bool explosionSpawned = false;
float manualRecord = 0f;

// modo treino
const string savePath = "treino_ia.dat";
var pop = new Population(rng);
bool loadedSave = pop.Load(savePath); // continua o treino de onde parou, se existir
if (loadedSave)
    Console.WriteLine($"Treino carregado de {savePath}: geracao {pop.Generation}, melhor pouso {pop.BestLandedAltEver:0.0} m");
int lastSaveGen = pop.Generation;
int stepsPerFrame = 6; // velocidade da simulacao
int leaderIdx = -1;    // foguete que a camera segue (o mais alto)

// modo assistir
var watchRocket = new Rocket(rng);
NeuralNet watchBrain = new NeuralNet(pop.BestEverGenome);
var watchInp = new float[NeuralNet.NIn];
float watchRespawn = 0f;

void StartWatch()
{
    watchBrain = new NeuralNet(pop.BestEverGenome);
    watchRocket.Reset();
    watchRespawn = 0f;
}

// escala da interface: 1.0 numa tela de 900px de altura, cresce em telas maiores
float uiScale = 1f;
int Fs(float size) => Math.Max(12, (int)(size * uiScale)); // tamanho de fonte escalado
void Txt(string s, float x, float y, float size, Color c) => DrawText(s, (int)x, (int)y, Fs(size), c);

while (!WindowShouldClose())
{
    float dt = GetFrameTime();
    if (dt > 0.05f) dt = 0.05f;
    int sw = GetScreenWidth();
    int sh = GetScreenHeight();
    uiScale = MathF.Max(1f, sh / 900f);

    // ---------------- troca de modo / janela ----------------
    if (IsKeyPressed(KeyboardKey.F)) ToggleBorderlessWindowed();
    if (IsKeyPressed(KeyboardKey.One)) mode = Mode.Manual;
    if (IsKeyPressed(KeyboardKey.Two)) mode = Mode.Train;
    if (IsKeyPressed(KeyboardKey.Three)) { mode = Mode.Watch; StartWatch(); }

    // ---------------- ATUALIZACAO ----------------
    if (mode == Mode.Manual)
    {
        if (IsKeyPressed(KeyboardKey.R) || IsKeyPressed(KeyboardKey.Space))
        {
            player.Reset();
            particles.Clear();
            explosionSpawned = false;
        }
        if (!player.Landed)
        {
            float t = 0f;
            if (IsKeyDown(KeyboardKey.A)) t -= 1f;
            if (IsKeyDown(KeyboardKey.D)) t += 1f;
            player.TorqueInput = t;
            if (IsKeyDown(KeyboardKey.W) || IsKeyDown(KeyboardKey.Up))
                player.Throttle += Config.ThrottleRate * dt;
            if (IsKeyDown(KeyboardKey.S) || IsKeyDown(KeyboardKey.Down))
                player.Throttle -= Config.ThrottleRate * dt;
            player.Throttle = Math.Clamp(player.Throttle, 0f, 1f);
        }
        player.Step(dt);
        if (player.MaxAltitude > manualRecord) manualRecord = player.MaxAltitude;
        if (player.Exploded && !explosionSpawned)
        {
            SpawnExplosion(player.Pos);
            explosionSpawned = true;
        }
    }
    else if (mode == Mode.Train)
    {
        if (IsKeyPressed(KeyboardKey.Up)) stepsPerFrame = Math.Min(40, stepsPerFrame + 1);
        if (IsKeyPressed(KeyboardKey.Down)) stepsPerFrame = Math.Max(1, stepsPerFrame - 1);
        for (int s = 0; s < stepsPerFrame; s++)
        {
            if (pop.AllDone()) { pop.Evolve(); leaderIdx = -1; break; }
            pop.Step(1f / 60f);
        }
        // a camera segue o lider; so troca de foguete quando o atual pousa
        if (leaderIdx < 0 || pop.Rockets[leaderIdx].Landed)
        {
            int h = pop.HighestAliveIndex();
            if (h >= 0) leaderIdx = h;
        }
        // salva automaticamente a cada 25 geracoes (nunca mais perde o treino!)
        if (pop.Generation - lastSaveGen >= 25)
        {
            pop.Save(savePath);
            lastSaveGen = pop.Generation;
        }
    }
    else // Watch
    {
        if (IsKeyPressed(KeyboardKey.R)) StartWatch();
        if (!watchRocket.Landed)
        {
            BuildWatchInputs(watchRocket, watchInp);
            watchBrain.Decide(watchInp, out float th, out float tq);
            watchRocket.Throttle = th;
            watchRocket.TorqueInput = tq;
            watchRocket.Step(dt);
        }
        else
        {
            watchRespawn += dt;
            if (watchRespawn > 2.5f) StartWatch(); // relanca sozinho
        }
    }

    // particulas (explosao no modo manual)
    for (int i = particles.Count - 1; i >= 0; i--)
    {
        var p = particles[i];
        p.Vel.Y += Config.Gravity * 0.5f * dt;
        p.Pos += p.Vel * dt;
        p.Life -= dt;
        if (p.Life <= 0f) particles.RemoveAt(i);
    }

    // ---------------- CAMERA ----------------
    Vector2 focus = mode switch
    {
        Mode.Manual => player.Pos,
        Mode.Watch => watchRocket.Pos,
        _ => (leaderIdx >= 0 ? pop.Rockets[leaderIdx].Pos : new Vector2(0f, Config.RestY))
    };
    // camera segue o foco (foguete) nos dois eixos, sem limite de altura.
    // Impede apenas de descer demais e mostrar muito abaixo do chao.
    Vector2 cam = new Vector2(focus.X - sw / 2f, focus.Y - sh * 0.65f);
    float maxCamY = Config.GroundY - sh * 0.35f;
    if (cam.Y > maxCamY) cam.Y = maxCamY;

    // ---------------- DESENHO ----------------
    BeginDrawing();
    ClearBackground(new Color(8, 10, 26, 255));
    DrawStars(cam, sw, sh);
    DrawGround(cam, sw, sh);

    if (mode == Mode.Manual)
    {
        DrawRocket(player, cam, true);
        foreach (var p in particles) DrawParticle(p, cam);
        DrawManualHud(player, manualRecord, sw, sh);
    }
    else if (mode == Mode.Train)
    {
        foreach (var r in pop.Rockets) DrawRocket(r, cam, false);
        if (leaderIdx >= 0)
        {
            Vector2 lp = pop.Rockets[leaderIdx].Pos - cam;
            DrawCircleLines((int)lp.X, (int)lp.Y, 26 * uiScale, Color.Yellow); // marca o lider
            DrawNetwork(pop.Brains[leaderIdx], sw);
        }
        float leaderAlt = leaderIdx >= 0 ? pop.Rockets[leaderIdx].AltitudeM : 0f;
        DrawTrainHud(pop, stepsPerFrame, leaderAlt, sw, sh);
    }
    else
    {
        DrawRocket(watchRocket, cam, true);
        DrawNetwork(watchBrain, sw);
        DrawWatchHud(watchRocket, pop, sw, sh);
    }

    Txt("1 Manual   2 Treino IA   3 Assistir   F tela cheia   R reinicia",
        20 * uiScale, sh - 30 * uiScale, 18, new Color(180, 180, 180, 255));
    EndDrawing();
}

pop.Save(savePath); // salva ao fechar a janela (ESC ou botao fechar)
CloseWindow();

// ======================= funcoes locais =======================

void SpawnExplosion(Vector2 at)
{
    for (int i = 0; i < 60; i++)
    {
        float a = rng.NextSingle() * MathF.PI * 2f;
        float sp = 80f + rng.NextSingle() * 340f;
        float life = 0.6f + rng.NextSingle() * 0.8f;
        particles.Add(new Particle
        {
            Pos = at,
            Vel = new Vector2(MathF.Cos(a) * sp, MathF.Sin(a) * sp - 80f),
            Life = life,
            MaxLife = life
        });
    }
}

void BuildWatchInputs(Rocket r, float[] inp)
{
    float rad = r.Angle * Config.Deg2Rad;
    inp[0] = r.AltitudeM / 300f;
    inp[1] = -r.Vel.Y / 300f;
    inp[2] = r.Vel.X / 300f;
    inp[3] = MathF.Sin(rad);
    inp[4] = MathF.Cos(rad);
    inp[5] = r.AngularVel / 180f;
    inp[6] = r.Fuel / Config.StartFuel;
}

// ---- Grafico da rede neural do lider (entradas -> ocultos -> saidas) ----
void DrawNetwork(NeuralNet net, int sw)
{
    float u = uiScale;
    float pw = 440 * u, ph = 240 * u;
    float px = sw - pw - 12 * u, py = 12 * u; // canto superior direito
    DrawRectangle((int)px, (int)py, (int)pw, (int)ph, new Color(0, 0, 0, 150));
    Txt("Rede neural do lider (ao vivo)", px + 12 * u, py + 8 * u, 18, Color.Lime);

    float colIn = px + 80 * u, colHid = px + pw / 2, colOut = px + pw - 84 * u;
    float top = py + 52 * u, usable = ph - 74 * u;

    Vector2 InPos(int i)  => new Vector2(colIn,  top + usable * (i + 0.5f) / NeuralNet.NIn);
    Vector2 HidPos(int h) => new Vector2(colHid, top + usable * (h + 0.5f) / NeuralNet.NHid);
    Vector2 OutPos(int o) => new Vector2(colOut, top + usable * (o + 0.5f) / NeuralNet.NOut);

    // conexoes (cor = sinal do peso, opacidade = forca)
    for (int h = 0; h < NeuralNet.NHid; h++)
        for (int i = 0; i < NeuralNet.NIn; i++)
            DrawLineEx(InPos(i), HidPos(h), 1f * u, WeightColor(net.InHidWeight(i, h)));
    for (int o = 0; o < NeuralNet.NOut; o++)
        for (int h = 0; h < NeuralNet.NHid; h++)
            DrawLineEx(HidPos(h), OutPos(o), 1.5f * u, WeightColor(net.HidOutWeight(h, o)));

    // neuronios (cor = ativacao atual)
    string[] inl = { "alt", "vY", "vX", "sin", "cos", "giro", "fuel" };
    for (int i = 0; i < NeuralNet.NIn; i++)
    {
        var p = InPos(i);
        DrawCircleV(p, 6 * u, ActColor(net.LastIn[i]));
        Txt(inl[i], p.X - 68 * u, p.Y - 8 * u, 15, Color.Gray);
    }
    for (int h = 0; h < NeuralNet.NHid; h++)
        DrawCircleV(HidPos(h), 7 * u, ActColor(net.LastHid[h]));

    string[] outl = { "motor", "giro" };
    float[] outv = { net.LastThrottle * 2f - 1f, net.LastTorque };
    for (int o = 0; o < NeuralNet.NOut; o++)
    {
        var p = OutPos(o);
        DrawCircleV(p, 8 * u, ActColor(outv[o]));
        Txt(outl[o], p.X + 14 * u, p.Y - 8 * u, 15, Color.Gray);
    }
}

// cor de uma conexao pelo peso: verde = positivo, vermelho = negativo
Color WeightColor(float w)
{
    float m = Math.Clamp(MathF.Abs(w) / 2f, 0f, 1f);
    byte a = (byte)(25 + 200 * m);
    return w >= 0 ? new Color((byte)70, (byte)200, (byte)120, a)
                  : new Color((byte)220, (byte)90, (byte)90, a);
}

// cor de um neuronio pela ativacao: laranja = positivo, azul = negativo
Color ActColor(float act)
{
    float m = Math.Clamp(MathF.Abs(act), 0f, 1f);
    byte c = (byte)(60 + 195 * m);
    return act >= 0 ? new Color(c, (byte)(c * 0.55f), (byte)40, (byte)255)
                    : new Color((byte)40, (byte)(c * 0.55f), c, (byte)255);
}

void DrawStars(Vector2 cam, int sw, int sh)
{
    foreach (var s in stars)
    {
        float sx = ((s.X - cam.X * 0.3f) % tileW + tileW) % tileW;
        float sy = ((s.Y - cam.Y * 0.3f) % tileH + tileH) % tileH;
        if (sx <= sw && sy <= sh)
            DrawCircle((int)sx, (int)sy, 1.2f, new Color(s.B, s.B, s.B, (byte)255));
    }
}

void DrawGround(Vector2 cam, int sw, int sh)
{
    float gy = Config.GroundY - cam.Y;
    DrawRectangle(0, (int)gy, sw, sh, new Color(30, 40, 30, 255));
    DrawLine(0, (int)gy, sw, (int)gy, new Color(90, 160, 90, 255));
    for (float wx = MathF.Floor(cam.X / 120f) * 120f; wx < cam.X + sw + 120f; wx += 120f)
    {
        int mx = (int)(wx - cam.X);
        DrawLine(mx, (int)gy, mx, (int)gy + 12, new Color(70, 110, 70, 255));
    }
}

void DrawParticle(Particle p, Vector2 cam)
{
    float t = Math.Clamp(p.Life / p.MaxLife, 0f, 1f);
    Vector2 sp = p.Pos - cam;
    DrawCircle((int)sp.X, (int)sp.Y, 2f + 4f * t,
               new Color((byte)255, (byte)(160 * t), (byte)40, (byte)(255 * t)));
}

void DrawRocket(Rocket r, Vector2 cam, bool detailed)
{
    Vector2 rp = r.Pos - cam;
    float rad = r.Angle * Config.Deg2Rad;
    float c = MathF.Cos(rad), s = MathF.Sin(rad);
    Vector2 Rot(float x, float y) => new Vector2(rp.X + x * c - y * s, rp.Y + x * s + y * c);

    if (r.Exploded)
    {
        // marca vermelha (no manual as particulas cuidam do visual)
        if (!detailed) DrawCircle((int)rp.X, (int)rp.Y, 5f, new Color(220, 60, 60, 200));
        return;
    }

    // chama
    if (r.Throttle > 0f && r.Fuel > 0f)
    {
        float flick = rng.NextSingle() * 8f;
        DrawTriangle(Rot(-6, 18), Rot(0, 32 + flick + r.Throttle * 18), Rot(6, 18), new Color(255, 170, 40, 255));
        DrawTriangle(Rot(-3, 18), Rot(0, 24 + flick * 0.5f + r.Throttle * 10), Rot(3, 18), new Color(255, 240, 180, 255));
    }

    // aletas
    DrawTriangle(Rot(-8, 6), Rot(-16, 20), Rot(-8, 20), new Color(200, 60, 60, 255));
    DrawTriangle(Rot(8, 6), Rot(8, 20), Rot(16, 20), new Color(200, 60, 60, 255));

    // corpo = tanque de combustivel
    Vector2 tl = Rot(-8, -6), tr = Rot(8, -6), bl = Rot(-8, 18), br = Rot(8, 18);
    DrawTriangle(tl, bl, br, new Color(45, 45, 55, 255));
    DrawTriangle(tl, br, tr, new Color(45, 45, 55, 255));
    float fr = r.Fuel / Config.StartFuel;
    float fillTop = 18f - 24f * fr;
    Color fuelColor = r.Fuel > 30 ? new Color(60, 200, 90, 255)
                    : r.Fuel > 10 ? new Color(230, 160, 40, 255)
                                  : new Color(220, 60, 60, 255);
    DrawTriangle(Rot(-7, fillTop), Rot(-7, 18), Rot(7, 18), fuelColor);
    DrawTriangle(Rot(-7, fillTop), Rot(7, 18), Rot(7, fillTop), fuelColor);
    if (detailed)
    {
        DrawLineEx(tl, bl, 1.5f, Color.LightGray);
        DrawLineEx(tr, br, 1.5f, Color.LightGray);
        DrawLineEx(bl, br, 1.5f, Color.LightGray);
    }
    DrawTriangle(Rot(0, -22), tl, tr, new Color(220, 80, 80, 255));
}

// centraliza um texto (com fonte escalada) na horizontal
void TxtCenter(string m, int sw, float y, float size, Color c)
{
    int fs = Fs(size);
    DrawText(m, sw / 2 - MeasureText(m, fs) / 2, (int)y, fs, c);
}

void DrawManualHud(Rocket r, float record, int sw, int sh)
{
    float u = uiScale;
    DrawRectangle((int)(10 * u), (int)(10 * u), (int)(280 * u), (int)(180 * u), new Color(0, 0, 0, 150));
    Txt($"Altitude: {r.AltitudeM,7:0.0} m", 22 * u, 22 * u, 22, Color.White);
    Txt($"Recorde:  {record,7:0.0} m", 22 * u, 50 * u, 22, Color.Gold);
    Txt($"Vel Y:    {-r.Vel.Y / Config.PixelsPerM,7:0.0} m/s", 22 * u, 78 * u, 20, Color.SkyBlue);
    Txt($"Vel X:    {r.Vel.X / Config.PixelsPerM,7:0.0} m/s", 22 * u, 104 * u, 20, Color.SkyBlue);
    Txt($"Angulo:   {Rocket.NormDeg(r.Angle),7:0.0} deg", 22 * u, 130 * u, 20, Color.SkyBlue);
    Color spin = MathF.Abs(r.AngularVel) > 90f ? Color.Red : Color.SkyBlue;
    Txt($"Giro:     {r.AngularVel,7:0.0} deg/s", 22 * u, 156 * u, 20, spin);

    float bx = sw - 240 * u;
    Txt("Forca do motor", bx, 22 * u, 20, Color.White);
    DrawRectangle((int)bx, (int)(52 * u), (int)(220 * u), (int)(20 * u), new Color(60, 60, 60, 255));
    DrawRectangle((int)bx, (int)(52 * u), (int)(220 * u * r.Throttle), (int)(20 * u), new Color(255, 140, 40, 255));

    if (r.Exploded)
        TxtCenter($"EXPLODIU!  {r.ImpactSpeed:0.0} m/s (limite {Config.MaxLandingSpeed:0.0}) - R reinicia", sw, sh / 2 - 14 * u, 28, Color.Red);
    else if (r.Landed)
        TxtCenter($"POUSO SUAVE!  {r.ImpactSpeed:0.0} m/s  -  R reinicia", sw, sh / 2 - 14 * u, 26, Color.Green);
    else if (!r.HasLaunched)
        TxtCenter("Segure W pra decolar!", sw, sh / 2 + 70 * u, 26, new Color(200, 200, 200, 255));
}

void DrawTrainHud(Population p, int speed, float leaderAlt, int sw, int sh)
{
    float u = uiScale;
    int alive = 0;
    foreach (var r in p.Rockets) if (!r.Landed) alive++;
    DrawRectangle((int)(10 * u), (int)(10 * u), (int)(390 * u), (int)(300 * u), new Color(0, 0, 0, 160));
    string fase = p.LandingPhase ? "== IA: PRATICANDO POUSO (etapa 1) ==" : "== IA: VOO COMPLETO (etapa 2) ==";
    Txt(fase, 22 * u, 18 * u, 22, p.LandingPhase ? Color.Orange : Color.Lime);
    Txt($"Geracao:        {p.Generation}", 22 * u, 48 * u, 22, Color.White);
    Txt($"Vivos:          {alive}/{Config.PopSize}", 22 * u, 76 * u, 20, Color.SkyBlue);
    Txt($"Altitude atual: {leaderAlt,7:0.0} m", 22 * u, 100 * u, 20, Color.White);
    Txt($"Altitude max:   {p.MaxAltitudeEver,7:0.0} m", 22 * u, 124 * u, 20, Color.SkyBlue);
    Txt($"Melhor pouso:   {p.BestLandedAltEver,7:0.0} m", 22 * u, 148 * u, 20, Color.Green);
    Txt($"Pousos suaves:  {p.LandedCount}", 22 * u, 172 * u, 20, Color.Green);
    Txt($"Melhor fitness: {p.BestEverFitness,7:0}", 22 * u, 196 * u, 20, Color.Gold);
    if (p.Reboots > 0)
        Txt($"Recomecos:      {p.Reboots}", 22 * u, 220 * u, 18, Color.Orange);
    Txt($"Velocidade x{speed}  (setas cima/baixo)", 22 * u, 246 * u, 18, new Color(180, 180, 180, 255));
    Txt("salvo automaticamente em treino_ia.dat", 22 * u, 270 * u, 16, new Color(120, 200, 120, 255));
}

void DrawWatchHud(Rocket r, Population p, int sw, int sh)
{
    float u = uiScale;
    DrawRectangle((int)(10 * u), (int)(10 * u), (int)(360 * u), (int)(148 * u), new Color(0, 0, 0, 160));
    Txt("== ASSISTINDO O MELHOR CEREBRO ==", 22 * u, 20 * u, 22, Color.Lime);
    Txt($"Fitness treinado: {p.BestEverFitness,7:0}", 22 * u, 52 * u, 20, Color.Gold);
    Txt($"Altitude:         {r.AltitudeM,7:0.0} m", 22 * u, 78 * u, 20, Color.White);
    Txt($"Combustivel:      {r.Fuel,7:0.0}", 22 * u, 104 * u, 20, Color.SkyBlue);
    if (r.Exploded)
        Txt($"EXPLODIU ({r.ImpactSpeed:0.0} m/s)", 22 * u, 130 * u, 20, Color.Red);
    else if (r.Landed)
        Txt($"POUSOU! ({r.ImpactSpeed:0.0} m/s)", 22 * u, 130 * u, 20, Color.Green);
    else
        Txt($"pico: {r.MaxAltitude:0.0} m", 22 * u, 130 * u, 20, new Color(180, 180, 180, 255));
}

// ======================= tipos =======================

enum Mode { Manual, Train, Watch }

class Particle
{
    public Vector2 Pos;
    public Vector2 Vel;
    public float Life;
    public float MaxLife;
}
