namespace GamePlay.ComponentSystems
{
    public enum EffectType : byte
    {
        None = 0,

        Idle = 1,
        Move = 2,
        Jump = 4,
        Fall = 6,
        Land = 8,
        Attack = 10,
        Die = 12,
        Hit = 14,
        Break = 16,
        Upgrade = 18,
    }
}
