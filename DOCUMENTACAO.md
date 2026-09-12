# 🚀 Foguete + IA — Documentação

Um joguinho de foguete em C# (Raylib-cs) com uma **inteligência artificial** que
aprende sozinha a subir o mais alto possível **e** pousar sem explodir, usando
**neuroevolução** (rede neural + algoritmo genético). Sem nenhuma biblioteca de
machine learning — é tudo C# puro.

> Esta é a documentação técnica detalhada. Para a visão geral do projeto, comece
> pelo **[README.md](README.md)**.

---

## Como rodar

```bash
cd ~/Documentos/FogueteGame
dotnet run
```

### Modos (troque com as teclas)

| Tecla | Modo | O que faz |
|-------|------|-----------|
| `1` | **Manual** | Você pilota o foguete. |
| `2` | **Treino IA** | Assiste o algoritmo genético evoluir 60 foguetes ao vivo. |
| `3` | **Assistir** | Vê o melhor cérebro já evoluído voar sozinho. |

> ⚠️ **O jogo começa no modo Manual.** Pra ver a IA você precisa apertar **`2`**.
> Se você só rodou e não viu a população, era isso. 🙂

### Controles gerais
- `A` / `D` — gira o foguete (torque)
- `W` / `S` — aumenta / diminui a força do motor
- `F` — tela cheia
- `R` — reinicia
- No treino: `↑` / `↓` — acelera / desacelera a simulação (mais passos por quadro)

### Teste sem janela (headless)
Pra ver a IA aprender só em texto (útil pra debugar):
```bash
dotnet run -- selftest 150      # evolui 150 gerações e imprime o fitness
```

---

## Arquitetura dos arquivos

| Arquivo | Responsabilidade |
|---------|------------------|
| `Config.cs` | Todas as constantes (física + IA) num lugar só. |
| `Rocket.cs` | O foguete e a física. Usado **igual** por humano e IA. |
| `Ai.cs` | A rede neural (`NeuralNet`) e o algoritmo genético (`Population`). |
| `Program.cs` | Loop principal, desenho, HUD e os 3 modos. |

A sacada central: **a física é a mesma pra todo mundo**. Alguém (o teclado ou a
rede neural) preenche `Throttle` e `TorqueInput` do foguete, e o `Rocket.Step(dt)`
avança a simulação. A IA não tem nenhuma "vantagem" — joga com as mesmas regras.

---

## A física (o "mundo")

Cada passo (`Rocket.Step`) faz, em vetores (`System.Numerics.Vector2`):

1. **Rotação com inércia.** O torque (do jogador ou da IA) muda a *velocidade
   angular*, não o ângulo direto. Existe ainda um **torque aerodinâmico
   desestabilizante**: se o foguete aponta pra um lado diferente de onde está
   indo, o "vento" amplifica o desvio — quanto mais rápido, mais fácil capotar.
2. **Empuxo.** Força na direção que o foguete aponta, dividida pela massa.
3. **Massa diminui** conforme queima combustível → fica mais leve → acelera mais.
4. **Gravidade** puxa pra baixo.
5. **Chão.** Se tocar rápido demais (> `MaxLandingSpeed`, hoje 8 m/s) → **explode**.

Todos os números ficam em `Config.cs`, comentados.

---

## A Inteligência Artificial

### Por que neuroevolução?

O problema não tem "resposta certa" rotulada pra ensinar (não dá pra fazer
aprendizado supervisionado). É um problema de **controle**: dada a situação,
qual ação tomar? A abordagem clássica e simples pra isso, sem framework de ML, é:

> Uma **rede neural** decide as ações; um **algoritmo genético** evolui os pesos
> da rede ao longo de gerações, favorecendo quem se sai melhor.

Não há backpropagation, nem gradientes, nem dataset. Só **seleção natural**
simulada. É literalmente evolução darwiniana aplicada a "cérebros" de foguete.

### 1) A rede neural — o "cérebro" (`NeuralNet`)

Uma rede *feed-forward* pequena:

```
7 entradas  →  8 neurônios ocultos (tanh)  →  2 saídas
```

**Entradas** (o que o foguete "sente", tudo normalizado pra ~[-1, 1]):

| # | Entrada |
|---|---------|
| 0 | altitude |
| 1 | velocidade vertical |
| 2 | velocidade horizontal |
| 3 | seno do ângulo |
| 4 | cosseno do ângulo |
| 5 | velocidade de giro |
| 6 | combustível |

> Usamos **seno e cosseno** do ângulo em vez do ângulo cru porque 359° e 1° são
> quase a mesma coisa fisicamente, mas números bem distantes — sen/cos evitam
> esse "salto" e ajudam a rede a aprender.

**Saídas:**
- `throttle` = sigmoide(saída 0) → força do motor de 0 a 1
- `torque` = tanh(saída 1) → giro de -1 (esquerda) a +1 (direita)

**O genoma** é simplesmente o vetor com *todos* os pesos e vieses da rede:
`8·(7+1) + 2·(8+1) = 82 números`. Evoluir o cérebro = evoluir esses 82 números.

### 2) O algoritmo genético (`Population`)

60 foguetes, cada um com seu cérebro. Um ciclo (**geração**) é:

```
1. SIMULAR   — todos voam ao mesmo tempo até pousarem ou o tempo acabar.
2. AVALIAR   — cada um ganha uma nota (fitness).
3. SELECIONAR— os melhores têm mais chance de "ter filhos".
4. REPRODUZIR— cruza pais + aplica mutação → nova geração.
   (repete)
```

- **Elitismo:** os 6 melhores passam intactos (não perdemos o que deu certo).
- **Seleção por torneio:** sorteia 4 candidatos, o melhor deles vira "pai". Isso
  dá vantagem aos bons sem eliminar totalmente a diversidade.
- **Crossover:** o filho herda cada peso de um dos dois pais, a esmo.
- **Mutação:** cada peso tem 12% de chance de sofrer um empurrãozinho aleatório
  (ruído gaussiano). É daqui que vêm as **novidades** — sem mutação, a evolução
  estagna.

### 3) A função de fitness — a parte mais importante (e mais traiçoeira!)

O fitness é **o que a IA realmente tenta maximizar**. Aqui mora a lição de ouro:

> A IA otimiza **exatamente o que você mede**, não o que você *queria* dizer.

Durante o desenvolvimento isso apareceu ao vivo:

- **1ª tentativa:** "nota = altitude máxima". Resultado: a IA aprendeu a subir
  ~9 km lindamente… e **nunca voltava**. Ela achou o truque de subir e deixar o
  tempo acabar no ar — altitude máxima sem o risco de pousar. (Isso tem nome:
  *reward hacking*.)
- **2ª tentativa:** dei prêmio grande por pousar. Mas "subir pra sempre" ainda
  valia mais, porque a recompensa de altura crescia sem limite. Continuou sem pousar.
- **Solução:** **limitar (cap)** o quanto "só subir" vale, e fazer o **prêmio de
  verdade só vir se voltar e pousar suave** — com um gradiente que recompensa
  *reduzir a velocidade de impacto* (guiando a IA de "explode forte" → "explode
  fraco" → "pousa"). Aí ela finalmente aprendeu a missão completa. ✅

A fórmula final (em `Population.Evolve`) tem uma versão por fase do currículo
(veja a seção do currículo mais abaixo):

**Fase 1 — praticando pouso** (o foguete nasce no ar):

```
spd  = velocidade atual (ou a de impacto, se já pousou), em m/s
slow = clamp(1 − spd / 150, 0, 1)     // faixa larga: há gradiente até em queda livre
nota = slow × 800

se pousou suave:  nota += 500 + combustível_restante
```

**Fase 2 — voo completo** (nasce no chão):

```
se pousou:
    lq   = clamp(1 − impacto / 25, 0, 1)      // qualidade do pouso, 0..1
    nota = lq × (500 + 6 × altitude_máxima)   // HeightReward = 6
    se não explodiu e realmente decolou:
        nota += 400 + combustível_restante
senão (ficou boiando no ar, nunca voltou):
    nota = 0
```

O detalhe decisivo da fase 2: a **altura é multiplicada pela qualidade do pouso**.
Pontos de altura só são "bancados" se o foguete chegar devagar — ou seja, para
lucrar com a subida ele precisa frear e quase pousar. Com isso, **voltar e pousar
sempre vale mais do que só subir**, e entre os que pousam, subir mais alto rende
mais — que é exatamente o objetivo do jogo.

### O que esperar ao assistir (modo `2`)

- **Gerações 1–20:** caos. Foguetes girando, explodindo, muitos nem decolam.
- **~Geração 25–40:** surgem os primeiros **pousos suaves** (contador verde sobe).
- **Depois:** a habilidade se espalha; costuma ficar em 7–11 pousos suaves por
  geração, subindo ~100 m e voltando inteiro.

Ir *muito* mais alto **e** pousar é um problema de controle genuinamente difícil
(é o desafio real de foguetes tipo Falcon 9!) — a evolução tende a achar uma
solução "conservadora" e segura. Dá pra empurrar mexendo nos pesos da fórmula de
fitness em `Ai.cs`, mas há um trade-off real entre altura e segurança.

---

## Isso já existe? (prior art)

Sim! E isso é um elogio ao projeto — você reinventou, do zero, uma abordagem que
é **pesquisa acadêmica de verdade** há mais de 20 anos:

- **"Active Guidance for a Finless Rocket Using Neuroevolution"** — Gomez &
  Miikkulainen, GECCO 2003 (ganhou o prêmio de melhor artigo em aplicações reais).
  É *quase exatamente este projeto*: um foguete **sem aletas** (aerodinamicamente
  instável, igual ao nosso `InstabFactor`) que é **impossível de voar sem controle
  ativo**, e eles evoluíram uma **rede neural** pra estabilizá-lo em tempo real.
- **NEAT (NeuroEvolution of Augmenting Topologies)** — Stanley & Miikkulainen, 2002.
  O algoritmo de neuroevolução mais famoso; evolui até a *topologia* da rede, não
  só os pesos (nós aqui usamos topologia fixa, que é a versão mais simples).
- **Lunar Lander** (OpenAI Gym) — o problema clássico de "pousar um foguete
  suavemente" em controle/RL. Resolvido por muita gente com algoritmo genético e
  com aprendizado por reforço (Deep Q-Learning etc.).
- **"IA aprendendo a pilotar FOGUETES!"** — canal **Universo Programado** (YouTube).
  Projeto quase idêntico a este: rede neural + estratégias evolutivas, 7 entradas,
  saídas contínuas, massa que muda com o combustível. A técnica de **treino em
  etapas** (decolar / navegar / pousar separados) que resolveu nosso muro veio dele.
- Existem **dezenas** de projetos de hobby "IA aprende a voar/pousar com rede
  neural + algoritmo genético" (estilo Code Bullet, Flappy Bird com NN+GA, etc.).

A diferença dos frameworks prontos (NEAT-Python, PyTorch, OpenAI Gym): aqui é
**tudo à mão em C#**, o que deixa cada peça visível e didática.

## A evolução da função de fitness (a saga)

Chegar na recompensa certa foi um estudo de caso vivo de *reward shaping*:

1. "Nota = altitude" → subia 9 km e **nunca voltava** (reward hacking).
2. Prêmio grande por pousar, mas fixo → só **pulinhos baixos** (ignora altura).
3. Altura só conta se pousar, mas com **abismo em 5 m/s** → travava "quase pousando".
4. Recompensa contínua + imigrantes + auto-recomeço → funcionava em ~metade das
   "seeds", mas na outra metade a população travava em "subir e cair a ~5 m/s"
   pra sempre. O muro real: **esparsidade de recompensa** (aprender decolar +
   voar + pousar tudo de uma vez é difícil demais).
5. **Solução definitiva — currículo (treino em etapas):** inspirado no vídeo do
   Universo Programado (veja abaixo). A população treina em **duas fases**:
   - **Etapa 1 — Praticar pouso:** o foguete **nasce no ar** em altura/velocidade
     aleatórias e só precisa aprender a **frear e pousar**. Sem a barreira de
     decolar, o pouso é descoberto rápido e de forma confiável.
   - **Etapa 2 — Voo completo:** quando já existe um bom pousador, a população
     **gradua** e passa a nascer no chão, decolar, subir e pousar — levando a
     habilidade de pousar junto.

   Com isso, até as "seeds" que antes eram impossíveis passaram a **subir ~1800 m
   e pousar suave**. O painel mostra a fase atual (PRATICANDO POUSO / VOO COMPLETO).

## Quer mexer? Ideias

- `Config.cs`: mude `MaxLandingSpeed`, `PopSize`, `MutationRate`, `InstabFactor`…
- `NeuralNet`: aumente `NHid` (neurônios ocultos) pra um cérebro mais "esperto".
- `Population.Evolve`: reescreva a fitness e veja a IA aprender coisas diferentes.
- Rode `dotnet run -- selftest 300` pra medir o efeito de cada mudança rápido.
