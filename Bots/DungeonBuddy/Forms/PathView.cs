// PathView.cs — DungeonBuddy navigation visualizer
// Ported from HB 4.3.4 Bots/DungeonBuddy/Forms/PathView.cs
// Opens as a standalone WinForms window (not embedded in FormConfig).
// Opened by the "Show Path" button on the Secret tab. Toggle: click again to close.
//
// What it shows (100ms refresh, GDI+ rendering):
//   Purple filled circles  = active avoidance zones (AvoidanceManager.Avoids)
//   Red dots + black lines = current nav path (CurrentMovePath or CurrentAvoidPath)
//   Green dot              = player position
//   Green line             = player heading direction (10 yard ray)
//
// Controls:
//   Mouse wheel  = zoom in/out (1× – 6×)
//   Left drag    = pan the view

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Bots.DungeonBuddy.Avoidance;
using Styx;
using Styx.Logic.Pathing;

namespace Bots.DungeonBuddy.Forms
{
    public class PathView : Form
    {
        #region Singleton

        /// <summary>
        /// Singleton instance. Null when the window is closed.
        /// </summary>
        public static PathView? Instance { get; private set; }

        #endregion

        #region Fields

        private WoWPoint _myLoc;
        private float _zoomRatio = 4f;

        private Graphics _gfx;
        private PointF _centerPos;
        private Bitmap _renderedImage;
        private Bitmap _gfxBitmap;

        private bool _isDragging;
        private Point _lastMousePoint;

        private readonly System.Windows.Forms.Timer _timer;

        #endregion

        #region Constructor

        public PathView()
        {
            Instance = this;

            Text = "PathView";
            ClientSize = new Size(784, 761);
            BackColor = Color.Black;
            DoubleBuffered = true;

            _gfxBitmap = new Bitmap(Width, Height);
            _gfx = Graphics.FromImage(_gfxBitmap);
            _gfx.SmoothingMode = SmoothingMode.HighQuality;

            _centerPos = new PointF(Width / 2f, Height / 2f);

            MouseWheel += OnMouseWheelZoom;

            _timer = new System.Windows.Forms.Timer { Interval = 100, Enabled = true };
            _timer.Tick += TimerTick;
            _timer.Start();
        }

        #endregion

        #region Input handlers

        private void OnMouseWheelZoom(object sender, MouseEventArgs e)
        {
            int delta = e.Delta / 120;
            if (delta < 0 && _zoomRatio > 1f) _zoomRatio = Math.Max(1f, _zoomRatio - 0.2f);
            if (delta > 0 && _zoomRatio < 6f) _zoomRatio = Math.Min(6f, _zoomRatio + 0.2f);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (!_isDragging)
                {
                    _isDragging = true;
                    _lastMousePoint = e.Location;
                }
                else
                {
                    // HB 4.3.4 parity: X axis maps to Y offset, Y axis maps to X offset.
                    int dx = e.X - _lastMousePoint.X;
                    int dy = e.Y - _lastMousePoint.Y;
                    _centerPos.X += dy;
                    _centerPos.Y += dx;
                    _lastMousePoint = e.Location;
                }
            }
            else if (_isDragging)
            {
                _isDragging = false;
            }
        }

        #endregion

        #region Paint override

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            _gfxBitmap?.Dispose();
            _gfx?.Dispose();
            _gfxBitmap = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
            _gfx = Graphics.FromImage(_gfxBitmap);
            _gfx.SmoothingMode = SmoothingMode.HighQuality;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_renderedImage != null)
                e.Graphics.DrawImage(_renderedImage, 0, 0);
            else
                e.Graphics.Clear(BackColor);
        }

        // Suppress default background erase to avoid flicker.
        protected override void OnPaintBackground(PaintEventArgs e) { }

        #endregion

        #region Close / dispose

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();

            if (Instance == this)
                Instance = null;

            _gfx?.Dispose();
            _gfxBitmap?.Dispose();
            _renderedImage?.Dispose();

            base.OnFormClosed(e);
        }

        #endregion

        #region Timer / render

        private void TimerTick(object sender, EventArgs e)
        {
            try
            {
                if (StyxWoW.Me == null) return;

                _gfx.Clear(BackColor);
                _myLoc = StyxWoW.Me.Location;

                // ── Avoidance circles (purple) ────────────────────────────────────
                foreach (var avoid in Bots.DungeonBuddy.Avoidance.AvoidanceManager.Avoids)
                {
                    DrawCircle(Color.MediumPurple, avoid.Location, avoid.Radius, 1);
                }

                // ── Navigation path (red dots + black lines) ──────────────────────
                var pathPoints = GetCurrentNavPath();
                if (pathPoints != null && pathPoints.Length > 0)
                {
                    if (pathPoints.Length == 1)
                    {
                        DrawPoint(Color.Red, pathPoints[0], 2f);
                        DrawLine(Color.Black, _myLoc, pathPoints[0], 1);
                    }
                    else
                    {
                        for (int i = 1; i < pathPoints.Length; i++)
                        {
                            DrawPoint(Color.Red, pathPoints[i], 2f);
                            DrawLine(Color.Black, pathPoints[i - 1], pathPoints[i], 1);
                        }
                    }
                }

                // ── Player position (green dot) + heading (green line) ────────────
                WoWPoint headingPoint = _myLoc.RayCast(StyxWoW.Me.Rotation, 10f);
                DrawPoint(Color.Green, _myLoc, 3f);
                DrawLine(Color.Green, _myLoc, headingPoint, 1);

                RenderImage(_gfxBitmap);
            }
            catch
            {
                // TimerTick runs on the UI thread but reads shared game state.
                // Any exception (game not running, mid-update) is silently ignored.
            }
        }

        /// <summary>
        /// Extracts the current nav path from the navigation provider via dynamic access,
        /// matching HB 4.3.4 PathView.Timer1Tick: prefers CurrentAvoidPath, falls back to
        /// CurrentMovePath.Path.Points.
        /// </summary>
        private WoWPoint[]? GetCurrentNavPath()
        {
            try
            {
                dynamic nav = Navigator.NavigationProvider;
                if (nav == null) return null;

                dynamic? rawPath = null;

                if (nav.CurrentAvoidPath != null)
                {
                    rawPath = nav.CurrentAvoidPath.Path;
                }
                else if (nav.CurrentMovePath != null && nav.CurrentMovePath.Path != null)
                {
                    rawPath = nav.CurrentMovePath.Path.Points;
                }

                if (rawPath == null) return null;

                var list = new List<WoWPoint>();
                foreach (dynamic pt in rawPath)
                    list.Add(new WoWPoint((float)pt.X, (float)pt.Y, (float)pt.Z));

                return list.ToArray();
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Drawing helpers

        /// <summary>
        /// Converts a WoW world coordinate to a 2D screen point using the current
        /// zoom ratio and center offset. Matches HB 4.3.4 PathView.WoWToScreen.
        /// </summary>
        private PointF WoWToScreen(WoWPoint point)
        {
            float dx = (_myLoc.X - point.X) * _zoomRatio;
            float dy = (_myLoc.Y - point.Y) * _zoomRatio;
            return new PointF(dy + _centerPos.Y, dx + _centerPos.X);
        }

        private void DrawCircle(Color color, WoWPoint pos, float radius, int thickness)
        {
            PointF screen = WoWToScreen(pos);
            float r = radius * _zoomRatio;
            using var pen = new Pen(color, thickness);
            using var fill = new SolidBrush(Color.FromArgb(60, color));
            _gfx.DrawEllipse(pen, screen.X - r, screen.Y - r, r * 2f, r * 2f);
            _gfx.FillEllipse(fill, screen.X - r, screen.Y - r, r * 2f, r * 2f);
        }

        private void DrawPoint(Color color, WoWPoint loc, float radius)
        {
            using var brush = new SolidBrush(color);
            PointF screen = WoWToScreen(loc);
            _gfx.FillEllipse(brush, screen.X - radius, screen.Y - radius, radius * 2f, radius * 2f);
        }

        private void DrawLine(Color color, WoWPoint start, WoWPoint end, int thickness)
        {
            using var pen = new Pen(color, thickness);
            _gfx.DrawLine(pen, WoWToScreen(start), WoWToScreen(end));
        }

        private void RenderImage(Bitmap source)
        {
            var newImg = new Bitmap(Width, Height);
            using var g = Graphics.FromImage(newImg);
            g.Clear(BackColor);
            g.DrawImage(source, 0, 0, Width, Height);

            var old = _renderedImage;
            _renderedImage = newImg;
            old?.Dispose();

            Refresh();
        }

        #endregion
    }
}
