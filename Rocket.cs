using System;
using System.Numerics;

// O foguete + a fisica. A MESMA classe eh usada pelo jogador e pela IA:
// alguem preenche Throttle e TorqueInput a cada passo e chama Step(dt).
class Rocket
{
    public Vector2 Pos;
    public Vector2 Vel;
    public float Angle;        // graus, 0 = pra cima
    public float AngularVel;   // graus/s (inercia de rotacao)
    public float Fuel;
    public float Throttle;     // 0..1 (forca do motor)
    public float TorqueInput;  // -1..1 (esquerda/direita) aplicado a cada passo

    public bool HasLaunched;
    public bool Landed;
    public bool Exploded;
    public float ImpactSpeed;  // m/s do toque no chao
    public float MaxAltitude;  // metros (recorde do voo)

    readonly Random _rng;

    public Rocket(Random rng)
    {
        _rng = rng;
        Reset();
    }

    public void Reset()
    {
        Pos = new Vector2(0f, Config.RestY);
        Vel = Vector2.Zero;
        Angle = 0f;
        AngularVel = 0f;
        Fuel = Config.StartFuel;
        Throttle = 0f;
        TorqueInput = 0f;
        HasLaunched = false;
        Landed = false;
        Exploded = false;
        ImpactSpeed = 0f;
        MaxAltitude = 0f;
    }

    // Nasce JA NO AR (pra praticar so o pouso - currículo da Etapa 3).
    public void ResetAir(float altitudePx, Vector2 vel, float angle)
    {
        Reset();
        Pos = new Vector2(0f, Config.RestY - altitudePx);
        Vel = vel;
        Angle = angle;
        HasLaunched = true; // ja esta voando
    }

    public float AltitudeM => MathF.Max(0f, Config.RestY - Pos.Y) / Config.PixelsPerM;

    public static float NormDeg(float d) => (d % 360f + 540f) % 360f - 180f;

    // Avanca a fisica em dt segundos.
    public void Step(float dt)
    {
        if (Landed) return;

        // ---- rotacao (inercia + torque do controlador + aerodinamica) ----
        AngularVel += TorqueInput * Config.TorquePower * dt;

        float speed = Vel.Length();
        if (speed > 1f)
        {
            float velAngle = MathF.Atan2(Vel.X, -Vel.Y) * Config.Rad2Deg;
            float diff = NormDeg(Angle - velAngle);
            AngularVel += Config.InstabFactor * speed * MathF.Sin(diff * Config.Deg2Rad) * dt;
        }
        if (Throttle > 0f && Fuel > 0f)
            AngularVel += (_rng.NextSingle() * 2f - 1f) * Config.ThrustNoise * dt;

        AngularVel -= AngularVel * Config.AngularDamp * dt;
        Angle += AngularVel * dt;

        // ---- empuxo + gravidade (vetorial) ----
        float rad = Angle * Config.Deg2Rad;
        Vector2 pointing = new Vector2(MathF.Sin(rad), -MathF.Cos(rad));
        float mass = Config.DryMass + Fuel * Config.FuelMass;

        if (Throttle > 0f && Fuel > 0f)
        {
            Vel += pointing * (Config.MaxThrust * Throttle / mass) * dt;
            Fuel -= Config.BurnRate * Throttle * dt;
            if (Fuel < 0f) Fuel = 0f;
        }

        Vel.Y += Config.Gravity * dt;
        Pos += Vel * dt;

        // ---- chao ----
        if (Pos.Y >= Config.RestY)
        {
            Pos.Y = Config.RestY;
            float impact = Vel.Length() / Config.PixelsPerM;

            // Bateu no chao vindo do ar, OU se jogou no chao descendo rapido
            // (mesmo sem ter "decolado" de verdade) -> pousa ou explode.
            if (HasLaunched || (Vel.Y > 0f && impact > Config.MaxLandingSpeed))
            {
                ImpactSpeed = impact;
                Landed = true;
                if (impact > Config.MaxLandingSpeed) Exploded = true;
                Vel = Vector2.Zero;
                AngularVel = 0f;
            }
            else
            {
                // Repousando na plataforma: nao afunda, e o atrito mata o
                // deslize lateral e o giro (senao ele "patina" pro lado).
                if (Vel.Y > 0f) Vel.Y = 0f;
                Vel.X *= 0.6f;
                AngularVel *= 0.4f;
            }
        }

        if (!HasLaunched && (Config.RestY - Pos.Y) > Config.LaunchGap)
            HasLaunched = true;

        if (AltitudeM > MaxAltitude) MaxAltitude = AltitudeM;
    }
}
