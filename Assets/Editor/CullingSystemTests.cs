using NUnit.Framework;
using UnityEngine;

public class CullingSystemTests
{
    [Test]
    public void TestCellDistanceCalculation()
    {
        CullingCell cell = new CullingCell
        {
            MinX = 10f,
            MaxX = 20f,
            MinZ = 10f,
            MaxZ = 20f
        };

        // 1. Point inside cell
        Assert.AreEqual(0f, cell.GetSqrDistanceToPoint(15f, 15f));

        // 2. Point directly to the right (x=25, z=15) -> dx = 5, dz = 0 -> sqrDist = 25
        Assert.AreEqual(25f, cell.GetSqrDistanceToPoint(25f, 15f));

        // 3. Point directly to the top (x=15, z=23) -> dx = 0, dz = 3 -> sqrDist = 9
        Assert.AreEqual(9f, cell.GetSqrDistanceToPoint(15f, 23f));

        // 4. Point diagonally bottom-left (x=5, z=5) -> dx = 5, dz = 5 -> sqrDist = 50
        Assert.AreEqual(50f, cell.GetSqrDistanceToPoint(5f, 5f));
    }

    [Test]
    public void TestCellGridInitialization()
    {
        GameObject go = new GameObject("TestCullingSystem");
        CullingComponent comp = go.AddComponent<CullingComponent>();
        CullingSystem system = go.AddComponent<CullingSystem>();

        // Setup config values manually via reflection or public setters
        comp.CellCountX = 3;
        comp.CellCountZ = 4;
        comp.CellSizeX = 10f;
        comp.CellSizeZ = 10f;

        // Initialize state inside System
        system.InitializeGrid();

        // System has 3 * 4 = 12 cells
        // In order to access private _state or cells, we can test GetCellFromPosition or modify CullingSystem to expose properties.
        // Let's test GetCellFromPosition logic since it verifies the grid creation and indexing.
        
        // 1. Center of Grid (x=15, z=25) -> Cell Index i=1 (10-20), j=2 (20-30)
        CullingCell cell = system.GetCellFromPosition(new Vector3(15f, 0f, 25f));
        Assert.IsNotNull(cell);
        Assert.AreEqual(1, cell.XIndex);
        Assert.AreEqual(2, cell.ZIndex);
        Assert.AreEqual(10f, cell.MinX);
        Assert.AreEqual(20f, cell.MaxX);
        Assert.AreEqual(20f, cell.MinZ);
        Assert.AreEqual(30f, cell.MaxZ);

        Object.DestroyImmediate(go);
    }

    [Test]
    public void TestGridPositionClamping()
    {
        GameObject go = new GameObject("TestCullingSystem");
        CullingComponent comp = go.AddComponent<CullingComponent>();
        CullingSystem system = go.AddComponent<CullingSystem>();

        comp.CellCountX = 3;
        comp.CellCountZ = 3;
        comp.CellSizeX = 10f;
        comp.CellSizeZ = 10f;

        system.InitializeGrid();

        // Position far to the left/bottom (-50, -50) -> should clamp to cell (0, 0)
        CullingCell cellBottomLeft = system.GetCellFromPosition(new Vector3(-50f, 0f, -50f));
        Assert.IsNotNull(cellBottomLeft);
        Assert.AreEqual(0, cellBottomLeft.XIndex);
        Assert.AreEqual(0, cellBottomLeft.ZIndex);

        // Position far to the right/top (100, 100) -> should clamp to cell (2, 2)
        CullingCell cellTopRight = system.GetCellFromPosition(new Vector3(100f, 0f, 100f));
        Assert.IsNotNull(cellTopRight);
        Assert.AreEqual(2, cellTopRight.XIndex);
        Assert.AreEqual(2, cellTopRight.ZIndex);

        Object.DestroyImmediate(go);
    }
}
