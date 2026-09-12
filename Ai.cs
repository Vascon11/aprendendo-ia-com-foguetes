using System;
using System.Globalization;
using System.IO;
using System.Text;

// ---------------------------------------------------------------------------
//  Rede neural feed-forward simples (o "cerebro" de um foguete).
//
//  Arquitetura fixa:  7 entradas -> 8 neuronios ocultos (tanh) -> 2 saidas
//  O "genoma" eh so o vetor de todos os pesos + vieses (bias). O algoritmo
//  genetico evolui esse vetor; nao ha backpropagation.
//
//  Entradas (normalizadas):
//    0 altitude   1 vel. vertical   2 vel. horizontal
//    3 sin(angulo) 4 cos(angulo)    5 vel. de giro    6 combustivel
//  Saidas:
//    0 throttle (via sigmoide 0..1)   1 torque (via tanh -1..1)
// ---------------------------------------------------------------------------
class NeuralNet
{
    public const int NIn  = 7;
    public const int NHid = 8;
    public const int NOut = 2;
    // pesos+bias da camada oculta e da de saida
    public const int GenomeSize = NHid * (NIn + 1) + NOut * (NHid + 1); // = 82

    public readonly float[] W; // genoma

    // ativacoes do ultimo Decide (pra desenhar a rede ao vivo)
    public readonly float[] LastIn = new float[NIn];
    public readonly float[] LastHid = new float[NHid];
    public float LastThrottle, LastTorque;

    public NeuralNet(float[] genome) { W = genome; }

    // acesso aos pesos por posicao (usado pelo grafico da rede)
    public float InHidWeight(int i, int h) => W[h * (NIn + 1) + i];
    public float HidOutWeight(int h, int o) => W[NHid * (NIn + 1) + o * (NHid + 1) + h];

    public static float[] RandomGenome(Random r)
    {
        var g = new float[GenomeSize];
        for (int i = 0; i < g.Length; i++) g[i] = r.NextSingle() * 2f - 1f; // [-1,1]
        return g;
    }

    // Preenche throttle (0..1) e torque (-1..1) a partir das entradas.
    public void Decide(float[] inp, out float throttle, out float torque)
    {
        for (int i = 0; i < NIn; i++) LastIn[i] = inp[i];
        int k = 0;

        for (int h = 0; h < NHid; h++)
        {
            float sum = 0f;
            for (int i = 0; i < NIn; i++) sum += inp[i] * W[k++];
            sum += W[k++]; // bias
            LastHid[h] = MathF.Tanh(sum);
        }

        Span<float> outp = stackalloc float[NOut];
        for (int o = 0; o < NOut; o++)
        {
            float sum = 0f;
            for (int h = 0; h < NHid; h++) sum += LastHid[h] * W[k++];
            sum += W[k++]; // bias
            outp[o] = sum;
        }

        throttle = 1f / (1f + MathF.Exp(-outp[0])); // sigmoide -> 0..1
        torque = MathF.Tanh(outp[1]);               // -1..1
        LastThrottle = throttle;
        LastTorque = torque;
    }
}

// ---------------------------------------------------------------------------
//  Populacao + algoritmo genetico.
//  Cada geracao: simula todos os foguetes, mede o fitness, seleciona os
//  melhores e gera a proxima geracao por crossover + mutacao.
// ---------------------------------------------------------------------------
class Population
{
    public readonly Rocket[] Rockets;
    public readonly NeuralNet[] Brains;
    public readonly float[] Fitness;

    public int Generation = 1;
    public float EpisodeTime = 0f;
    public float BestFitness = 0f;       // melhor da geracao passada
    public float BestEverFitness = 0f;
    public float[] BestEverGenome;       // melhor cerebro ja visto
    public int LandedCount = 0;          // pousos suaves na geracao passada
    public float MaxAltitudeEver = 0f;    // maior altitude ja alcancada por qualquer foguete
    public float BestLandedAltEver = 0f;  // maior altitude de onde alguem ja pousou suave
    public int Reboots = 0;               // quantas vezes recomecou do zero por travar
    public bool LandingPhase = true;      // currículo: comeca praticando SO o pouso (nasce no ar)
    int _stuckGens = 0;                   // geracoes seguidas sem nenhum pouso
    int _goodLandingGens = 0;             // geracoes seguidas pousando bem (pra graduar)

    readonly Random _rng;
    readonly float[] _inp = new float[NeuralNet.NIn];

    public Population(Random rng)
    {
        _rng = rng;
        Rockets = new Rocket[Config.PopSize];
        Brains = new NeuralNet[Config.PopSize];
        Fitness = new float[Config.PopSize];
        for (int i = 0; i < Config.PopSize; i++)
        {
            Rockets[i] = new Rocket(rng);
            Brains[i] = new NeuralNet(NeuralNet.RandomGenome(rng));
        }
        BestEverGenome = (float[])Brains[0].W.Clone();
        NewEpisode();
    }

    // Prepara o proximo episodio: reposiciona todos os foguetes conforme a fase.
    public void NewEpisode()
    {
        for (int i = 0; i < Rockets.Length; i++)
        {
            if (LandingPhase)
            {
                // Etapa 3 (Pouso): nasce NO AR, altura/velocidade/angulo aleatorios,
                // e so precisa aprender a descer e pousar. Sem a barreira de decolar.
                float alt = 600f + _rng.NextSingle() * 1200f;        // 60..180 m
                float vx = (_rng.NextSingle() * 2f - 1f) * 30f;      // deriva lateral
                float vy = _rng.NextSingle() * 30f;                  // 0..30 caindo
                float ang = (_rng.NextSingle() * 2f - 1f) * 25f;     // inclinacao inicial
                Rockets[i].ResetAir(alt, new System.Numerics.Vector2(vx, vy), ang);
            }
            else
            {
                Rockets[i].Reset(); // voo completo: nasce no chao
            }
        }
        EpisodeTime = 0f;
    }

    public bool AllDone()
    {
        if (EpisodeTime >= Config.MaxEpisodeTime) return true;
        foreach (var r in Rockets) if (!r.Landed) return false;
        return true;
    }

    // indice do foguete vivo mais alto (o "lider"); -1 se todos pousaram
    public int HighestAliveIndex()
    {
        int idx = -1;
        float hi = float.MinValue;
        for (int i = 0; i < Rockets.Length; i++)
        {
            var r = Rockets[i];
            if (!r.Landed && -r.Pos.Y > hi) { hi = -r.Pos.Y; idx = i; }
        }
        return idx;
    }

    // Cada foguete vivo "pensa" e a fisica avanca um passo.
    public void Step(float dt)
    {
        foreach (var (r, i) in Enumerate())
        {
            if (r.Landed) continue;
            BuildInputs(r);
            Brains[i].Decide(_inp, out float throttle, out float torque);
            r.Throttle = throttle;
            r.TorqueInput = torque;
            r.Step(dt);
        }
        EpisodeTime += dt;
    }

    void BuildInputs(Rocket r)
    {
        float rad = r.Angle * Config.Deg2Rad;
        _inp[0] = r.AltitudeM / 300f;
        _inp[1] = -r.Vel.Y / 300f;
        _inp[2] = r.Vel.X / 300f;
        _inp[3] = MathF.Sin(rad);
        _inp[4] = MathF.Cos(rad);
        _inp[5] = r.AngularVel / 180f;
        _inp[6] = r.Fuel / Config.StartFuel;
    }

    // Mede o fitness, cria a proxima geracao e reinicia o episodio.
    public void Evolve()
    {
        LandedCount = 0;
        for (int i = 0; i < Config.PopSize; i++)
        {
            Rocket r = Rockets[i];
            float climb = r.MaxAltitude;
            if (climb > MaxAltitudeEver) MaxAltitudeEver = climb;

            float f;
            if (LandingPhase)
            {
                // ETAPA 3 (pratica de pouso): nasceu no ar. Recompensa DENSA por
                // ir devagar (freou = bom), com faixa larga pra ter gradiente ate
                // da velocidade terminal. Vale pra pousado E pra quem ainda voa
                // devagar (aprende a "segurar" a queda). Bonus por pousar de fato.
                float spd = r.Landed ? r.ImpactSpeed : r.Vel.Length() / Config.PixelsPerM;
                float slow = Math.Clamp(1f - spd / 150f, 0f, 1f);
                f = slow * 800f;
                if (r.Landed && !r.Exploded) { f += 500f + r.Fuel; LandedCount++; }
            }
            else if (r.Landed) // voo completo: voltou e tocou o chao
            {
                float lq = Math.Clamp(1f - r.ImpactSpeed / 25f, 0f, 1f);
                // A ALTURA so eh "bancada" multiplicada pela qualidade: pra ganhar
                // pontos de altura voce precisa chegar devagar (=frear=quase pousar).
                f = lq * (500f + Config.HeightReward * climb);
                if (!r.Exploded && r.HasLaunched)
                {
                    f += 400f + r.Fuel; // pousou de verdade
                    LandedCount++;
                    if (climb > BestLandedAltEver) BestLandedAltEver = climb;
                }
            }
            else
            {
                f = 0f; // boiando, nunca voltou
            }

            Fitness[i] = MathF.Max(0f, f);
        }


        // ordena indices por fitness (desc)
        int[] order = new int[Config.PopSize];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        Array.Sort(order, (a, b) => Fitness[b].CompareTo(Fitness[a]));

        BestFitness = Fitness[order[0]];
        if (BestFitness > BestEverFitness)
        {
            BestEverFitness = BestFitness;
            BestEverGenome = (float[])Brains[order[0]].W.Clone();
        }

        // GRADUACAO do currículo: quando existe um bom pousador consistente
        // (nao precisa a populacao toda), passa da pratica de pouso pro voo
        // completo. Os elites levam a habilidade de pousar junto.
        if (LandingPhase)
        {
            if (BestFitness >= 1100f) _goodLandingGens++; else _goodLandingGens = 0;
            if (_goodLandingGens >= 8)
            {
                LandingPhase = false;
                _goodLandingGens = 0;
                BestEverFitness = 0f; // zera: a escala de fitness do voo completo eh outra
            }
        }

        // monta os genomas da proxima geracao
        var next = new float[Config.PopSize][];
        for (int e = 0; e < Config.Elites; e++)              // elitismo
            next[e] = (float[])Brains[order[e]].W.Clone();
        int childEnd = Config.PopSize - Config.Immigrants;
        for (int i = Config.Elites; i < childEnd; i++)       // filhos (crossover + mutacao)
        {
            float[] pa = Brains[Tournament(order)].W;
            float[] pb = Brains[Tournament(order)].W;
            next[i] = Mutate(Crossover(pa, pb));
        }
        for (int i = childEnd; i < Config.PopSize; i++)      // imigrantes aleatorios (diversidade)
            next[i] = NeuralNet.RandomGenome(_rng);

        // Anti-travamento: SO durante a pratica de pouso. Se ela nao aprender a
        // pousar (nascendo no ar!) em muitas geracoes, algo esta bem estranho ->
        // recomeca com cerebros novos. Depois de graduar, NUNCA reinicia (senao
        // apagaria a habilidade de pousar ja aprendida).
        if (LandedCount > 0) _stuckGens = 0; else _stuckGens++;
        if (LandingPhase && _stuckGens >= Config.RerollStuckGens)
        {
            for (int i = 0; i < Config.PopSize; i++) next[i] = NeuralNet.RandomGenome(_rng);
            _stuckGens = 0;
            Reboots++;
        }

        // aplica os novos cerebros e prepara o proximo episodio (respeita a fase)
        for (int i = 0; i < Config.PopSize; i++)
            Brains[i] = new NeuralNet(next[i]);
        NewEpisode();
        Generation++;
    }

    int Tournament(int[] order)
    {
        int best = order[_rng.Next(Config.PopSize)];
        for (int i = 1; i < Config.TournamentK; i++)
        {
            int c = order[_rng.Next(Config.PopSize)];
            if (Fitness[c] > Fitness[best]) best = c;
        }
        return best;
    }

    float[] Crossover(float[] a, float[] b)
    {
        var c = new float[a.Length];
        for (int i = 0; i < c.Length; i++) c[i] = _rng.NextSingle() < 0.5f ? a[i] : b[i];
        return c;
    }

    float[] Mutate(float[] g)
    {
        for (int i = 0; i < g.Length; i++)
            if (_rng.NextSingle() < Config.MutationRate)
                g[i] += NextGaussian() * Config.MutationStdDev;
        return g;
    }

    // ruido gaussiano (Box-Muller)
    float NextGaussian()
    {
        float u1 = 1f - _rng.NextSingle();
        float u2 = 1f - _rng.NextSingle();
        return MathF.Sqrt(-2f * MathF.Log(u1)) * MathF.Cos(2f * MathF.PI * u2);
    }

    System.Collections.Generic.IEnumerable<(Rocket, int)> Enumerate()
    {
        for (int i = 0; i < Rockets.Length; i++) yield return (Rockets[i], i);
    }

    // ------------------- Salvar / Carregar o treino -------------------
    // Formato texto simples. IMPORTANTE: usa cultura invariante (ponto decimal),
    // senao a maquina em pt-BR salvaria com virgula e nao leria de volta.
    static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    static string GenomeToStr(float[] g)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < g.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(g[i].ToString("G9", Ci));
        }
        return sb.ToString();
    }

    static float[] StrToGenome(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var g = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++) g[i] = float.Parse(parts[i], Ci);
        return g;
    }

    public void Save(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FOGUETE-IA v2");
        sb.AppendLine(Generation.ToString(Ci));
        sb.AppendLine(BestEverFitness.ToString(Ci));
        sb.AppendLine(MaxAltitudeEver.ToString(Ci));
        sb.AppendLine(BestLandedAltEver.ToString(Ci));
        sb.AppendLine(Reboots.ToString(Ci));
        sb.AppendLine(LandingPhase ? "1" : "0");
        sb.AppendLine(GenomeToStr(BestEverGenome));
        sb.AppendLine(Config.PopSize.ToString(Ci));
        for (int i = 0; i < Config.PopSize; i++)
            sb.AppendLine(GenomeToStr(Brains[i].W));
        File.WriteAllText(path, sb.ToString());
    }

    // Retorna true se carregou com sucesso.
    public bool Load(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            var lines = File.ReadAllLines(path);
            int k = 0;
            if (lines[k++].Trim() != "FOGUETE-IA v2") return false;
            Generation = int.Parse(lines[k++], Ci);
            BestEverFitness = float.Parse(lines[k++], Ci);
            MaxAltitudeEver = float.Parse(lines[k++], Ci);
            BestLandedAltEver = float.Parse(lines[k++], Ci);
            Reboots = int.Parse(lines[k++], Ci);
            LandingPhase = lines[k++].Trim() == "1";

            var best = StrToGenome(lines[k++]);
            if (best.Length == NeuralNet.GenomeSize) BestEverGenome = best;

            int n = int.Parse(lines[k++], Ci);
            for (int i = 0; i < Config.PopSize; i++)
            {
                if (i < n)
                {
                    var g = StrToGenome(lines[k++]);
                    // se o tamanho do genoma mudou (arquitetura diferente), ignora
                    if (g.Length != NeuralNet.GenomeSize) return false;
                    Brains[i] = new NeuralNet(g);
                }
                else
                {
                    // salvou menos cerebros que a populacao atual: completa aleatorio
                    Brains[i] = new NeuralNet(NeuralNet.RandomGenome(_rng));
                }
            }
            NewEpisode(); // reposiciona conforme a fase carregada
            return true;
        }
        catch { return false; }
    }
}
