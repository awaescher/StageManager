namespace StageManager.Native.Window
{
    public class WindowLocation : IWindowLocation
    {
        public WindowLocation(int x, int y, int width, int height, WindowState state)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            State = state;
        }

        public int X { get; private set; }
        public int Y { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public WindowState State { get; private set; }

        public bool IsPointInside(int x, int y)
        {
            return X <= x && x <= X + Width && Y <= y && y <= Y + Height;
        }

        public override string ToString()
        {
            return $"{State} - {X}:{Y}/{Width}:{Height}";
        }
    }
}
