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

using ForceDirectedGraph;

using Clifton.ExtensionMethods;

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
		public static double FONT_WEIGHT_MULTIPLIER = 16.0;

		protected Brush blackBrush;
		public Brush whiteBrush;
		public Brush greenBrush;
		protected Pen pen;
		protected Pen whitePen;
		protected Font font;
		protected Point surfaceOffset = new Point(0, 0);
		protected List<Pen> penColors;
		protected Point mouseStart;
		protected Point mousePosition;
		protected bool dragSurface;
		protected bool changeZoom;
		protected double scaleFactor = 1.0;

		// Model for the surface
		protected string keyword;
		protected List<SentenceInfo> previousKeywords;
		protected List<SentenceInfo> nextKeywords;
		
		// Yes, I know this is terrible entanglement.
		public Dictionary<Rectangle, string> keywordLocationMap;

		public Surface()
		{
			blackBrush = new SolidBrush(Color.Black);
			whiteBrush = new SolidBrush(Color.White);
			greenBrush = new SolidBrush(Color.Green);
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

			keywordLocationMap = new Dictionary<Rectangle, string>();
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
			e.Graphics.FillRectangle(blackBrush, new Rectangle(new Point(0, 0), Size));
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
			e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

			if (Program.app.directedGraph)
			{
				DrawForceDirectedGraph(e.Graphics);
			}
			else
			{
				DrawNeighboringSentenceKeywords(e.Graphics);
			}
		}

		protected void DrawNeighboringSentenceKeywords(Graphics gr)
		{
			try
			{
				// Get location of keyword in the center of the
				Point ctr = new Point(Size.Width / 2, Size.Height / 2);

				keywordLocationMap.Clear();
				DrawPreviousKeywords(gr, ctr);
				DrawNextKeywords(gr, ctr);
				DrawKeyword(gr, keyword);		// Last, so that text appears above lines.
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine(ex.Message);
			}
		}

		protected void DrawForceDirectedGraph(Graphics gr)
		{
			keywordLocationMap.Clear();
			Program.app.mDiagram.Draw(gr, new Rectangle(20, 20, Size.Width - 40, Size.Height - 40), scaleFactor);
		}

		protected void DrawKeyword(Graphics gr, string keyword)
		{
			Point center = new Point(Size.Width / 2, Size.Height / 2);
			double relevance;
			Font font;

			if (Program.app.keywordRelevanceMap.TryGetValue(keyword, out relevance))
			{
				font = new Font(FontFamily.GenericSansSerif, (float)(8.0 + (Program.app.keywordRelevanceMap[keyword] - Program.app.minRelevance) * FONT_WEIGHT_MULTIPLIER));
			}
			else
			{
				font = new Font(FontFamily.GenericSansSerif, 8);
			}

			SizeF strSize = gr.MeasureString(keyword, font);
			Point textCenter = Point.Subtract(center, new Size((int)strSize.Width / 2, (int)strSize.Height / 2));
			gr.DrawString(keyword, font, whiteBrush, SurfaceOffsetAdjust(textCenter));
			font.Dispose();
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
				// Vertical starting location.
				int starty = source.Y - 25 * (n / 2);
				
				if ((n & 1) == 0)
				{
					starty -= 25;
				}

				keywords.ForEach(si =>
					{
						Point edge = new Point(source.X + hOffset, starty);
						Font font = new Font(FontFamily.GenericSansSerif, (float)(8.0 + (si.Relevance - Program.app.minRelevance) * FONT_WEIGHT_MULTIPLIER));
						SizeF strSize = gr.MeasureString(si.Keyword, font);
						Point textCenter = Point.Subtract(edge, new Size((int)strSize.Width / 2, (int)strSize.Height / 2));
						Rectangle r = new Rectangle(textCenter, new Size((int)strSize.Width, (int)strSize.Height));
						keywordLocationMap[r] = si.Keyword;
						gr.DrawLine(penColors[2], SurfaceOffsetAdjust(edge), SurfaceOffsetAdjust(source));
						gr.DrawString(si.Keyword, font, whiteBrush, SurfaceOffsetAdjust(textCenter));
						starty += 50;
						font.Dispose();
					});
			}
		}

		protected void MouseDownEvent(object sender, MouseEventArgs args)
		{
			if (args.Button == MouseButtons.Left)
			{
				changeZoom = true;
				mouseStart = args.Location;
				mousePosition = args.Location;
			}
			else if (args.Button == MouseButtons.Right)
			{
				dragSurface = true;
				mouseStart = NegativeSurfaceOffsetAdjust(args.Location);
				mousePosition = NegativeSurfaceOffsetAdjust(args.Location);
			}
		}

		protected void MouseUpEvent(object sender, MouseEventArgs args)
		{
			changeZoom = false;
			dragSurface = false;
		}

		protected void MouseMoveEvent(object sender, MouseEventArgs args)
		{
			mousePosition = args.Location;
			Point delta = Point.Subtract(args.Location, new Size(mouseStart));

			if (dragSurface)
			{
				surfaceOffset = delta;
				Invalidate(true);
			}
			else if (changeZoom)
			{
				scaleFactor += (delta.X + delta.Y) / 100.0;

				(scaleFactor < 0.2).Then(() => scaleFactor = 0.2);
				(scaleFactor > 10.0).Then(() => scaleFactor = 10.0);

				mouseStart = args.Location;

				Invalidate(true);
			}
		}

		protected void MouseDoubleClickEvent(object sender, MouseEventArgs args)
		{
			Point p = NegativeSurfaceOffsetAdjust(args.Location);

			foreach(KeyValuePair<Rectangle, string> kvp in keywordLocationMap)
			{
				if (kvp.Key.Contains(p))
				{
					Program.app.SelectKeyword(kvp.Value);
					// Program.app.ShowKeywordSelection(kvp.Value);
					break;
				}
			}
		}

		protected void MouseWheelEvent(object sender, MouseEventArgs args)
		{
			double spin = args.Delta / 120;			// Where does this constant come from?

			scaleFactor += spin / 10;

			(scaleFactor < 0.2).Then(() => scaleFactor = 0.2);
			(scaleFactor > 10.0).Then(() => scaleFactor = 10.0);

			Invalidate(true);
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

		public Rectangle SurfaceOffsetAdjust(Rectangle r)
		{
			return new Rectangle(SurfaceOffsetAdjust(r.Location), r.Size);
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
