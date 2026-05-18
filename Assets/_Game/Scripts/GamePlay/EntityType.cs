namespace GamePlay.Entities
{
    public enum EntityType : short
    {
        None = 0, // ignore

        Wheel = 1,
        Character = 2,
        Item = 3,
        Enemy = 4,
        EnemyWeapon = 5,
        ResourceTower = 6,
        PlayerWeapon = 7,  // Player projectiles/weapons
        CapacityFactory = 8,
        CapacityGate = 9,
        PowerGate = 10,
        SpeedBoard = 12,
        Obstacle = 14,
        FinishTrigger = 16,
        FinishTower = 18,
        GateNewEra = 20,

        
        All = 255
    }
}
