# 🚀 Foguete + IA — um foguete que aprende a voar sozinho

Um jogo de foguete em **C# + Raylib** onde uma inteligência artificial aprende,
do zero, a **decolar, subir e pousar sem explodir** — usando **neuroevolução**
(rede neural controlada por um algoritmo genético).

Sem PyTorch, sem TensorFlow, sem NEAT-Python, sem dataset: **tudo escrito à mão
em C# puro**, em ~1.000 linhas. Cada peça do aprendizado está visível no código.

```
       /\            7 sensores  →  8 neurônios  →  2 ações
      |  |           (altitude, velocidade, ângulo, combustível…)
      |IA|                              ↓
      /__\                    throttle  +  torque
      ^^^^           60 foguetes por geração · seleção natural simulada
```

---

## Índice

- [Rodando o projeto](#rodando-o-projeto)
- [Como a IA funciona](#como-a-ia-funciona)
  - [1. O cérebro: a rede neural](#1-o-cérebro-a-rede-neural)
  - [2. O genoma](#2-o-genoma)
  - [3. O algoritmo genético](#3-o-algoritmo-genético)
  - [4. A função de fitness](#4-a-função-de-fitness)
  - [5. O currículo (treino em duas etapas)](#5-o-currículo-treino-em-duas-etapas)
- [Como a IA foi desenvolvida](#como-a-ia-foi-desenvolvida-a-saga-do-fitness)
- [Resultados](#resultados)
- [Arquitetura do código](#arquitetura-do-código)
- [Quer mexer?](#quer-mexer)
- [Prior art](#isso-já-existe-prior-art)

---

## Rodando o projeto

Requer **.NET 8 SDK**. O Raylib-cs vem via NuGet, não precisa instalar nada além.

```bash
dotnet run
```

O jogo **abre no modo Manual**. Aperte **`2`** para ver a IA treinando ao vivo.

| Tecla | Modo | O que faz |
|-------|------|-----------|
| `1` | **Manual** | Você pilota o foguete. |
| `2` | **Treino IA** | Assiste 60 foguetes evoluírem geração após geração. |
| `3` | **Assistir** | Vê o melhor cérebro já evoluído voar sozinho. |

**Controles:** `A`/`D` gira · `W`/`S` motor · `F` tela cheia · `R` reinicia ·
`↑`/`↓` (no treino) acelera/desacelera a simulação · `ESC` sai.

O treino é salvo automaticamente em `treino_ia.dat` e **continua de onde parou**
na próxima execução.

### Treinar sem janela (headless)

Útil para medir o efeito de uma mudança rapidamente:

```bash
dotnet run -- selftest 150        # evolui 150 gerações e imprime o progresso
dotnet run -- selftest 150 42     # com outra seed aleatória
```

---

## Como a IA funciona

O problema **não tem resposta certa rotulada**: não existe um dataset de "nesta
situação, o throttle correto é 0,73". É um problema de **controle** — dado o
estado do foguete, qual ação tomar? Aprendizado supervisionado está fora.

A solução aqui é a mais direta que existe sem framework de ML:

> Uma **rede neural** decide as ações. Um **algoritmo genético** evolui os pesos
> dessa rede ao longo das gerações, favorecendo quem se sai melhor.

Não há backpropagation, nem gradiente, nem otimizador. Só **seleção natural
simulada** — evolução darwiniana aplicada a cérebros de foguete.

### 1. O cérebro: a rede neural

Uma rede *feed-forward* minúscula (`NeuralNet`, em [`Ai.cs`](Ai.cs)):

```
7 entradas  →  8 neurônios ocultos (tanh)  →  2 saídas
```

**Entradas** — o que o foguete "sente", tudo normalizado para ~[-1, 1]:

| # | Sensor | Normalização |
|---|--------|--------------|
| 0 | altitude | `altitude_m / 300` |
| 1 | velocidade vertical | `-vy / 300` |
| 2 | velocidade horizontal | `vx / 300` |
| 3 | **seno** do ângulo | — |
| 4 | **cosseno** do ângulo | — |
| 5 | velocidade de giro | `giro / 180` |
| 6 | combustível | `fuel / 100` |

> **Por que seno e cosseno em vez do ângulo?** Porque 359° e 1° são quase a mesma
> orientação física, mas números distantes — a rede teria que aprender essa
> descontinuidade sozinha. Com sen/cos o ângulo vira um par contínuo, sem salto.
> Normalizar as entradas importa pelo mesmo motivo: pesos aleatórios em [-1, 1]
> não conseguiriam lidar com uma entrada que vale 1800 e outra que vale 0,3.

**Saídas** — as duas ações, passadas por ativações que as prendem no domínio certo:

- `throttle = sigmoide(saída₀)` → força do motor, **0 a 1**
- `torque = tanh(saída₁)` → giro, **-1 (esquerda) a +1 (direita)**

A mesma classe `Rocket` é usada pelo jogador e pela IA: alguém preenche
`Throttle` e `TorqueInput`, e `Rocket.Step(dt)` avança a física. **A IA não tem
nenhuma vantagem** — joga com exatamente as mesmas regras que você.

### 2. O genoma

O "DNA" de um foguete é simplesmente o vetor com **todos os pesos e vieses** da
rede, achatado num `float[]`:

```
8 × (7 + 1)  +  2 × (8 + 1)  =  82 números
 ↑ camada oculta   ↑ camada de saída
```

Evoluir um cérebro = evoluir esses **82 números**. Nada mais.

### 3. O algoritmo genético

60 foguetes, cada um com seu genoma. Uma **geração** é o ciclo (`Population`):

```
┌─ 1. SIMULAR ──── todos voam ao mesmo tempo até pousar ou o tempo acabar (45 s)
│  2. AVALIAR ──── cada um recebe uma nota (fitness)
│  3. SELECIONAR ─ os melhores têm mais chance de gerar filhos
└─ 4. REPRODUZIR ─ crossover + mutação → nova população
```

Os mecanismos, todos configuráveis em [`Config.cs`](Config.cs):

| Mecanismo | Valor | Para que serve |
|-----------|-------|----------------|
| **Elitismo** | 6 melhores | Passam **intactos** para a geração seguinte — o que deu certo nunca se perde. |
| **Seleção por torneio** | K = 4 | Sorteia 4 candidatos, o melhor vira pai. Dá vantagem aos bons **sem** eliminar a diversidade (roleta pura converge cedo demais). |
| **Crossover uniforme** | 50/50 | Cada um dos 82 pesos do filho vem de um dos dois pais, sorteado. |
| **Mutação gaussiana** | 12% · σ=0,4 | De onde vêm as **novidades**. Sem mutação a evolução estagna no que já existe. |
| **Imigrantes** | 5 por geração | Genomas 100% aleatórios injetados todo ciclo, para escapar de ótimos locais. |
| **Anti-travamento** | 100 gerações | Se a fase de pouso não produzir *nenhum* pouso em 100 gerações, recomeça com cérebros novos (uma seed ruim não trava o treino para sempre). |

### 4. A função de fitness

Esta é a parte mais importante do projeto — e a mais traiçoeira. A lição:

> **A IA otimiza exatamente o que você mede, não o que você queria dizer.**

A fórmula atual (`Population.Evolve`) tem duas versões, uma por fase do currículo:

**Fase 1 — praticando pouso** (o foguete nasce no ar):

```
spd  = velocidade atual (ou de impacto, se já pousou), em m/s
slow = clamp(1 − spd / 150, 0, 1)         // faixa larga = gradiente contínuo
nota = slow × 800
se pousou suave:  nota += 500 + combustível_restante
```

**Fase 2 — voo completo** (nasce no chão):

```
se pousou:
    lq   = clamp(1 − impacto / 25, 0, 1)   // qualidade do pouso, 0..1
    nota = lq × (500 + 6 × altitude_máxima)
    se não explodiu e decolou de verdade:
        nota += 400 + combustível_restante
senão (ficou boiando, nunca voltou):
    nota = 0
```

Repare no detalhe decisivo da fase 2: a **altura é multiplicada pela qualidade do
pouso**. Subir alto só vale pontos se você voltar devagar. Isso fecha o buraco
que fez a primeira versão da IA subir 9 km e nunca mais voltar — veja a seguir.

### 5. O currículo (treino em duas etapas)

Aprender *decolar + navegar + pousar* tudo de uma vez é difícil demais: a
recompensa é **esparsa** — no começo, nenhum foguete aleatório chega perto de um
pouso suave, então não há gradiente nenhum para a evolução seguir.

A solução foi treinar em etapas, como se ensina qualquer habilidade complexa:

**Etapa 1 — só o pouso.** O foguete **nasce no ar**, com altura (60–180 m),
velocidade e inclinação aleatórias. Não precisa decolar; só precisa aprender a
frear e tocar o chão devagar. Sem a barreira da decolagem, o pouso é descoberto
em ~25 gerações, de forma confiável.

**Etapa 2 — voo completo.** Quando um bom pousador aparece de forma consistente
(fitness ≥ 1100 por 8 gerações seguidas), a população **gradua**: passa a nascer
no chão e a missão vira decolar, subir e voltar — **levando junto** a habilidade
de pousar que os elites já carregam no genoma.

O painel do modo treino mostra a fase atual (`PRATICANDO POUSO` / `VOO COMPLETO`).

---

## Como a IA foi desenvolvida (a saga do fitness)

Nenhuma dessas versões foi "planejada": cada uma nasceu de assistir a IA fazer
algo tecnicamente perfeito e completamente errado. É um estudo de caso ao vivo de
*reward shaping*.

**1ª tentativa — "nota = altitude máxima".**
A IA aprendeu a subir ~9 km lindamente… e **nunca voltava**. Descobriu que subir
e deixar o tempo acabar no ar maximiza a altitude sem o risco de pousar. Isso tem
nome na literatura: ***reward hacking*** — a política ótima para a métrica não é
a que você queria.

**2ª tentativa — prêmio grande por pousar.**
Continuou sem pousar. O prêmio era grande, mas a recompensa por altura crescia
**sem limite** — então "subir para sempre" ainda pagava mais. Quando duas
recompensas competem, quem não tem teto ganha.

**3ª tentativa — altura só conta se pousar.**
Melhorou, mas apareceu outro vício: como o bônus de pouso era um degrau em 5 m/s
(pousou / explodiu, sem meio-termo), a população travava **"quase pousando"** a
5,2 m/s para sempre. Um degrau não dá gradiente: a evolução não tinha como saber
que estava chegando perto.

**4ª tentativa — recompensa contínua + diversidade.**
Troquei o degrau por uma rampa (`1 − impacto/25`), que recompensa *reduzir* a
velocidade de impacto — guiando a IA de "explode forte" → "explode fraco" →
"pousa". Adicionei imigrantes aleatórios e o auto-recomeço. Passou a funcionar
em **cerca de metade das seeds**; na outra metade a população travava em "subir e
cair a 5 m/s" indefinidamente.

**5ª solução — currículo.**
O muro real não era a fórmula, era a **esparsidade**: aprender as três
habilidades simultaneamente é improvável demais para uma busca aleatória. Separar
o treino em *praticar pouso* → *voo completo* resolveu de vez. Seeds que antes
eram impossíveis passaram a subir e pousar suave de forma consistente.

O resumo da lição, se você for escrever sua própria fitness:

1. **Toda métrica sem teto vira a única estratégia.** Limite ou condicione.
2. **Degraus não ensinam; rampas ensinam.** Recompensa contínua = gradiente.
3. **Se a recompensa é rara demais, ninguém a encontra.** Quebre a tarefa em etapas.
4. **Assista a IA trapacear.** O comportamento absurdo dela é sempre o relatório
   de bug da sua função de fitness.

---

## Resultados

Saída real de `dotnet run -- selftest 60` (seed padrão 7):

```
Ger    1 [POUSO]: melhor=    541   pousos=0     ← caos total
Ger   26 [POUSO]: melhor=   1297   pousos=1     ← primeiro pouso suave
Ger   31 [POUSO]: melhor=   1297   pousos=10    ← a habilidade se espalha
Ger   36 [VOO  ]: melhor=   1309   pousos=9     ← graduou para o voo completo
Ger   51 [VOO  ]: melhor=   1244   melhor_pouso= 105,8m  pousos=7
Ger   60 [VOO  ]: melhor=   1239   melhor_pouso= 105,8m  pousos=8
```

Assistindo no modo `2`, o padrão típico é: caos nas primeiras gerações, primeiros
pousos por volta da 25ª, graduação por volta da 35ª, e daí em diante voos cada
vez mais altos com 7–11 pousos suaves por geração. Rodando por algumas centenas
de gerações, dá para ver o recorde de "pousou vindo de" subir bastante.

Ir **muito** mais alto **e** pousar continua sendo um problema de controle
genuinamente difícil — é o desafio real de um Falcon 9. A evolução tende a achar
uma solução conservadora e segura; empurrar a altura mexendo em `HeightReward`
custa taxa de pouso. O trade-off é real.

---

## Arquitetura do código

| Arquivo | Responsabilidade |
|---------|------------------|
| [`Config.cs`](Config.cs) | Todas as constantes (física + IA) num lugar só, comentadas. |
| [`Rocket.cs`](Rocket.cs) | O foguete e a física. Usado **igual** por humano e IA. |
| [`Terrain.cs`](Terrain.cs) | Relevo do chão: plano no spawn, morros conforme você se afasta. |
| [`Ai.cs`](Ai.cs) | A rede neural (`NeuralNet`) e o algoritmo genético (`Population`). |
| [`Program.cs`](Program.cs) | Loop principal, render, HUD, os 3 modos e o selftest headless. |

### A física (o "mundo")

Cada passo de `Rocket.Step(dt)`, em vetores (`System.Numerics.Vector2`):

1. **Rotação com inércia** — o torque muda a *velocidade angular*, não o ângulo.
2. **Instabilidade aerodinâmica** — se o foguete aponta para um lado diferente de
   onde está indo, o "vento" **amplifica** o desvio. Quanto mais rápido, mais
   fácil capotar. É o que torna o controle ativo obrigatório.
3. **Empuxo** na direção em que o foguete aponta, dividido pela massa.
4. **Massa cai** conforme queima combustível → fica mais leve → acelera mais.
5. **Gravidade** puxa para baixo.
6. **Chão** — tocar acima de `MaxLandingSpeed` (8 m/s) **explode**.

### Persistência

`Population.Save/Load` grava a geração, os recordes, a fase do currículo, o
melhor genoma de todos os tempos e os 60 cérebros atuais num arquivo texto.
Detalhe que custou um bug: a serialização usa **`CultureInfo.InvariantCulture`** —
numa máquina em pt-BR, `float.ToString()` gravaria vírgula decimal e o arquivo
não voltaria a ser lido.

---

## Quer mexer?

- **[`Config.cs`](Config.cs)** — `MaxLandingSpeed`, `PopSize`, `MutationRate`,
  `InstabFactor`, `HeightReward`… mude um número e rode o selftest.
- **`NeuralNet.NHid`** — mais neurônios ocultos = cérebro com mais capacidade
  (e um espaço de busca maior para a evolução varrer).
- **`Population.Evolve`** — reescreva a fitness e veja a IA aprender outra coisa.
  Vale a pena: é a melhor forma de sentir na pele as quatro lições acima.
- Compare seeds: `dotnet run -- selftest 200 <seed>`.

---

## Isso já existe? (prior art)

Sim — e isso é um elogio ao projeto: a abordagem aqui é **pesquisa acadêmica de
verdade** há mais de 20 anos.

- **"Active Guidance for a Finless Rocket Using Neuroevolution"** — Gomez &
  Miikkulainen, GECCO 2003 (melhor artigo em aplicações reais). É quase
  exatamente este projeto: um foguete **sem aletas** — aerodinamicamente
  instável, igual ao nosso `InstabFactor` — que é impossível de voar sem controle
  ativo, estabilizado por uma rede neural evoluída.
- **NEAT (NeuroEvolution of Augmenting Topologies)** — Stanley & Miikkulainen,
  2002. O algoritmo de neuroevolução mais famoso; evolui até a *topologia* da
  rede, não só os pesos. Aqui usamos topologia fixa, a versão mais simples.
- **Lunar Lander** (OpenAI Gym) — o problema clássico de pousar suavemente,
  resolvido tanto por algoritmos genéticos quanto por RL (Deep Q-Learning etc.).
- **"IA aprendendo a pilotar FOGUETES!"** — canal *Universo Programado*. Projeto
  muito próximo deste; a ideia de **treino em etapas** que derrubou nosso muro
  veio de lá.

A diferença para os frameworks prontos (NEAT-Python, PyTorch, Gym): aqui é tudo à
mão, o que deixa cada peça visível — e é esse o ponto.

---

Para o mergulho didático completo, veja **[DOCUMENTACAO.md](DOCUMENTACAO.md)**.
