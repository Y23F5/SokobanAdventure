/**
 * @file Cell.cs
 * @brief Cell 是不依赖 UnityEngine 的二维整数格坐标，用于规则层。
 * Cell(int,int)：构造格坐标。
 * operator +、-：逐分量加减，用于位移与方向计算。
 * Equals、GetHashCode、operator ==、!=：按分量比较，可作字典键。
 * Up、Right、Down、Left、SearchOrder：四个方向与固定搜索顺序（上、右、下、左），保证同一局面结果相同。
 */
using System;

namespace Sokoban.Core
{
    public readonly struct Cell : IEquatable<Cell>
    {
        public readonly int X;
        public readonly int Y;

        public Cell(int x, int y) { X = x; Y = y; }

        public static readonly Cell Up = new Cell(0, 1);
        public static readonly Cell Right = new Cell(1, 0);
        public static readonly Cell Down = new Cell(0, -1);
        public static readonly Cell Left = new Cell(-1, 0);
        public static readonly Cell[] SearchOrder = { Up, Right, Down, Left };

        public static Cell operator +(Cell a, Cell b) => new Cell(a.X + b.X, a.Y + b.Y);
        public static Cell operator -(Cell a, Cell b) => new Cell(a.X - b.X, a.Y - b.Y);
        public static bool operator ==(Cell a, Cell b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Cell a, Cell b) => !(a == b);

        public bool Equals(Cell other) => this == other;
        public override bool Equals(object obj) => obj is Cell other && this == other;
        public override int GetHashCode() => unchecked(X * 397 ^ Y);
        public override string ToString() => $"({X},{Y})";
    }
}
