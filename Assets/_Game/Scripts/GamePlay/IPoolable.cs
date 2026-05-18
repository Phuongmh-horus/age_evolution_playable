// File: IPoolable.cs
public interface IPoolable
{
    void New();  // Gọi khi Spawn
    void Free(); // Gọi khi Despawn
}