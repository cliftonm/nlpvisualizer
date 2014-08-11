using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NlpVisualizer
{
	public class VisualizerControl : Panel
	{
		public VisualizerControl()
		{
			SetStyle(ControlStyles.DoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
		}
	}

	public class Surface : UserControl
	{
		protected Brush blackBrush;
		protected Brush whiteBrush;
		protected Pen pen;
		protected Pen whitePen;
		protected Font font;
		protected Point surfaceOffset = new Point(0, 0);
		protected List<Pen> penColors;
		protected Point mouseStart;
		protected Point mousePosition;
		protected bool dragSurface;

		// Model for the surface
		protected string keyword;
		List<SentenceInfo> previousKeywords;
		List<SentenceInfo> nextKeywords;

		public Surface()
		{
			blackBrush = new SolidBrush(Color.Black);
			whiteBrush = new SolidBrush(Color.White);
			font = new Font(FontFamily.GenericSansSerif, 8);
			pen = new Pen(Color.LightBlue);
			whitePen = new Pen(Color.White);
			penColors = new List<Pen>();

			penColors.Add(new Pen(Color.Red));
			penColors.Add(new Pen(Color.Green));
			penColors.Add(new Pen(Color.Blue));
			penColors.Add(new Pen(Color.Yellow));
			penColors.Add(new Pen(Color.Cyan));
			penColors.Add(new Pen(Color.Magenta));
			penColors.Add(new Pen(Color.Lavender));
			penColors.Add(new Pen(Color.Purple));
			penColors.Add(new Pen(Color.Salmon));

		}

		public void NewKeyword(string keyword)
		{
			this.keyword = keyword;
			surfaceOffset = new Point(0, 0);
		}

		public void PreviousKeywords(List<SentenceInfo> prevKeywords)
		{
			this.previousKeywords = prevKeywords;
		}

		public void NextKeywords(List<SentenceInfo> nextKeywords)
		{
			this.nextKeywords = nextKeywords;
		}

		protected void OnVisualizerPaint(object sender, PaintEventArgs e)
		{
			try
			{
				Control ctrl = (Control)sender;

				e.Graphics.FillRectangle(blackBrush, new Rectangle(Location, Size));
				e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
				e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

				Point ctr = DrawKeyword(e.Graphics, keyword);
				DrawPreviousKeywords(e.Graphics, ctr);
				DrawNextKeywords(e.Graphics, ctr);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine(ex.Message);
			}
		}

		protected Point DrawKeyword(Graphics gr, string keyword)
		{
			Point center = new Point(Location.X + Size.Width / 2, Location.Y + Size.Height / 2);
			SizeF strSize = gr.MeasureString(keyword, font);
			Point textCenter = Point.Subtract(center, new Size((int)strSize.Width / 2, (int)strSize.Height / 2));
			gr.DrawString(keyword, font, whiteBrush, SurfaceOffsetAdjust(textCenter));

			return center;
		}

		protected void DrawPreviousKeywords(Graphics gr, Point source)
		{
			DrawAdjacentKeywords(gr, source, previousKeywords, -200);
		}

		protected void DrawNextKeywords(Graphics gr, Point source)
		{
			DrawAdjacentKeywords(gr, source, nextKeywords, 200);
		}

		protected void DrawAdjacentKeywords(Graphics gr, Point source, List<SentenceInfo> keywords, int hOffset)
		{
			int n = keywords.Count;

			if (n > 0)
			{
				int starty = source.Y - 25 * (n / 2);
				
				if ((n & 1) == 0)
				{
					starty -= 25;
				}

				keywords.ForEach(si =>
					{
						Point edge = new Point(source.X + hOffset, starty);
						SizeF strSize = gr.MeasureString(si.Keyword, font);
						Point textCenter = Point.Subtract(edge, new Size((int)strSize.Width / 2, (int)strSize.Height / 2));
						gr.DrawString(si.Keyword, font, whiteBrush, SurfaceOffsetAdjust(textCenter));
						gr.DrawLine(penColors[2], SurfaceOffsetAdjust(edge), SurfaceOffsetAdjust(source));
						starty += 50;
					});
			}
		}

		protected void MouseDownEvent(object sender, MouseEventArgs args)
		{
			if (args.Button == MouseButtons.Right)
			{
				dragSurface = true;
				mouseStart = NegativeSurfaceOffsetAdjust(args.Location);
				mousePosition = NegativeSurfaceOffsetAdjust(args.Location);
			}
		}

		protected void MouseMoveEvent(object sender, MouseEventArgs args)
		{
			mousePosition = args.Location;

			if (dragSurface)
			{
				base.OnMouseMove(args);
				surfaceOffset = Point.Subtract(args.Location, new Size(mouseStart));
				Invalidate(true);
			}
		}

		/// <summary>
		/// Returns a point adjusted (adding) for the surface offset.
		/// </summary>
		public Point SurfaceOffsetAdjust(Point src)
		{
			Point p = src;
			p.Offset(surfaceOffset);

			return p;
		}

		/// <summary>
		/// Returns a point adjusted for the surface offset by subtracting the current surface offset.
		/// </summary>
		public Point NegativeSurfaceOffsetAdjust(Point src)
		{
			Point p = src;
			p.Offset(-surfaceOffset.X, -surfaceOffset.Y);

			return p;
		}
	}
}
