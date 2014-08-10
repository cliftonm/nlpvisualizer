using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

using AlchemyAPI;

using Clifton.ExtensionMethods;
using Clifton.MycroParser;

namespace NlpVisualizer
{
	public static class RichTextBoxExtensions
	{
		public static void AppendText(this RichTextBox box, string text, Color color)
		{
			box.SelectionStart = box.TextLength;
			box.SelectionLength = 0;

			box.SelectionColor = color;
			box.AppendText(text);
			box.SelectionColor = box.ForeColor;
		}
	}

	public class Program
	{
		protected Form form;
		protected TextBox tbUrl;
		protected DataGridView dgvKeywords;
		protected StatusBarPanel sbStatus;
		protected Button btnProcess;
		protected Label lblAlchemyKeywords;
		protected RichTextBox rtbSentences;
		protected Button btnPrevSentence;
		protected Button btnNextSentence;
		
		protected AlchemyWrapper alchemy;
		protected DataSet dsKeywords;
		protected DataView dvKeywords;
		protected string pageText;
		protected List<string> pageSentences;
		protected List<int> displayedSentenceIndices;
		protected int currentSentenceIdx;
		protected bool textboxEventsEnabled;
		protected string keyword;

		public Program()
		{
			displayedSentenceIndices = new List<int>();
		}

		public void Initialize()
		{
			MycroParser mp = new MycroParser();
			XmlDocument doc = new XmlDocument();
			doc.Load("MainForm.xml");
			mp.Load(doc, "Form", this);
			form = (Form)mp.Process();

			dgvKeywords = (DataGridView)mp.ObjectCollection["dgvKeywords"];
			sbStatus = mp.ObjectCollection["sbStatus"] as StatusBarPanel;
			btnProcess = mp.ObjectCollection["btnProcess"] as Button;
			tbUrl = mp.ObjectCollection["tbUrl"] as TextBox;
			lblAlchemyKeywords = (Label)mp.ObjectCollection["lblAlchemyKeywords"];
			rtbSentences = (RichTextBox)mp.ObjectCollection["rtbSentences"];
			btnPrevSentence = (Button)mp.ObjectCollection["btnPrevSentence"];
			btnNextSentence = (Button)mp.ObjectCollection["btnNextSentence"];
				
			InitializeNlp();
			Application.Run(form);
		}

		protected async void InitializeNlp()
		{
			sbStatus.Text = "Initializing NLP's...";

			alchemy = await Task.Run(() =>
			{
				AlchemyWrapper api = new AlchemyWrapper();
				api.Initialize();
				return api;
			});

			btnProcess.Enabled = true;
			sbStatus.Text = "Ready";
		}

		protected async void Process(object sender, EventArgs args)
		{
			btnProcess.Enabled = false;
			ClearAllGrids();
			string url = tbUrl.Text;
			sbStatus.Text = "Acquiring page content...";
			pageText = await Task.Run(() => GetUrlText(url));
			pageSentences = ParseOutSentences(pageText);
			sbStatus.Text = "Acquiring keywords from AlchemyAPI...";
			dsKeywords = GetKeywords(url, pageText);
			dvKeywords = new DataView(dsKeywords.Tables["keyword"]);
			dgvKeywords.DataSource = dvKeywords;
			lblAlchemyKeywords.Text = String.Format("Keywords: {0}", dvKeywords.Count);
			btnProcess.Enabled = true;
		}

		protected void OnKeywordSelection(object sender, EventArgs args)
		{
			DataGridViewSelectedRowCollection rows = dgvKeywords.SelectedRows;

			if (rows.Count > 0)
			{
				DataGridViewRow row = rows[0];
				keyword = row.Cells[0].Value.ToString();
				textboxEventsEnabled = false;
				ShowSentences(keyword);
				textboxEventsEnabled = true;
				rtbSentences.SelectionStart = 0;
			}
		}

		protected void OnCursorMoved(object sender, EventArgs args)
		{
			if (textboxEventsEnabled)
			{
				int pos = rtbSentences.SelectionStart;
				currentSentenceIdx = FindSentence(pos);

				btnPrevSentence.Enabled = currentSentenceIdx > 0;
				btnNextSentence.Enabled = currentSentenceIdx < pageSentences.Count - 1;
			}
		}

		protected void OnPreviousSentence(object sender, EventArgs args)
		{
			currentSentenceIdx--;
			ShowSentence(currentSentenceIdx);
		}

		protected void OnNextSentence(object sender, EventArgs args)
		{
			currentSentenceIdx++;
			ShowSentence(currentSentenceIdx);
		}

		protected void ShowSentence(int idx)
		{
			textboxEventsEnabled = false;
			ShowSentenceWithKeywords(pageSentences[idx], keyword);
			btnPrevSentence.Enabled = idx > 0;
			btnNextSentence.Enabled = idx < pageSentences.Count - 1;

			// Clear the displayed sentence index list and add only the sentence we are currently displaying.
			displayedSentenceIndices.Clear();
			displayedSentenceIndices.Add(idx);

			textboxEventsEnabled = true;
			rtbSentences.SelectionStart = 0;
		}

		protected int FindSentence(int charIdx)
		{
			int sentenceIdx = -1;
			int totalChars = 0;

			foreach (int idx in displayedSentenceIndices)
			{
				int sentenceLength = pageSentences[idx].Length;

				if (totalChars + sentenceLength > charIdx)
				{
					sentenceIdx = idx;
					break;
				}

				totalChars += sentenceLength + 2;		// +2 for the \n\n.
			}

			return sentenceIdx;
		}

		protected void ShowSentences(string keyword)
		{
			rtbSentences.Clear();
			displayedSentenceIndices.Clear();

			pageSentences.ForEachWithIndex((sentence, sidx) =>
			{
				string s = sentence.ToLower();
				int idx = s.IndexOf(keyword);
				bool found = idx >= 0;
				int start = 0;

				while (idx >= 0)
				{
					// Remember the index of this sentence, but we don't want duplicates.
					if (!displayedSentenceIndices.Contains(sidx))
					{
						displayedSentenceIndices.Add(sidx);
					}

					// Use master sentence to preserve casing.
					string substr = sentence.Substring(start, idx);
					rtbSentences.AppendText(substr);
					rtbSentences.AppendText(keyword, Color.Red);

					// Get remainder.
					s = s.Substring(idx + keyword.Length);
					start += idx + keyword.Length;		// for master sentence.
					idx = s.IndexOf(keyword);
				}

				if (found)
				{
					// Append the remainder.
					rtbSentences.AppendText(s);
					rtbSentences.AppendText("\n\n");
				}
			});
		}

		protected void ShowSentenceWithKeywords(string sentence, string keyword)
		{
			rtbSentences.Clear();
			string s = sentence.ToLower();
			int idx = s.IndexOf(keyword);
			bool found = idx >= 0;
			int start = 0;

			while (idx >= 0)
			{
				// Use master sentence to preserve casing.
				string substr = sentence.Substring(start, idx);
				rtbSentences.AppendText(substr);
				rtbSentences.AppendText(keyword, Color.Red);

				// Get remainder.
				s = s.Substring(idx + keyword.Length);
				start += idx + keyword.Length;		// for master sentence.
				idx = s.IndexOf(keyword);
			}

			if (found)
			{
				// Append the remainder.
				rtbSentences.AppendText(s);
			}
			else
			{
				// No keywords, so append the whole text.
				rtbSentences.AppendText(sentence);
			}
		}

		/// <summary>
		/// Return the keyword dataset either from the cache or by using AlchemyAPI to extract the keywords.
		/// </summary>
		protected DataSet GetKeywords(string url, string pageText)
		{
			string urlHash = url.GetHashCode().ToString();
			string keywordFilename = urlHash + ".keywords";
			DataSet dsKeywords = null;

			if (File.Exists(keywordFilename))
			{
				dsKeywords = new DataSet();
				dsKeywords.ReadXml(keywordFilename);
			}
			else
			{
				dsKeywords = alchemy.LoadKeywords(pageText);
				dsKeywords.WriteXml(keywordFilename, XmlWriteMode.WriteSchema);
			}

			return dsKeywords;
		}

		/// <summary>
		/// Return the text of the URL either from the cache or by using AlchemyAPI to extract the text.
		/// </summary>
		protected string GetUrlText(string url)
		{
			string urlHash = url.GetHashCode().ToString();
			string textFilename = urlHash + ".txt";
			string pageText;

			if (File.Exists(textFilename))
			{
				pageText = File.ReadAllText(textFilename);
			}
			else
			{
				pageText = GetPageText(url);
			}

			File.WriteAllText(textFilename, pageText);

			return pageText;
		}

		/// <summary>
		/// We use AlchemyAPI to get the page text for OpenCalais and Semantria.
		/// </summary>
		protected string GetPageText(string url)
		{
			AlchemyWrapper alchemy = new AlchemyWrapper();
			alchemy.Initialize();

			string xml = alchemy.GetUrlText(url);
			XmlDocument xdoc = new XmlDocument();
			xdoc.LoadXml(xml);

			return xdoc.SelectSingleNode("//text").InnerText;
		}

		protected void ClearAllGrids()
		{
			dgvKeywords.DataSource = null;
		}

		/// <summary>
		/// Simple processor that converts the text into a list of sentences.
		/// </summary>
		protected List<string> ParseOutSentences(string page)
		{
			// Split by periods.
			// Trim edges of whitespace.
			// Ignore empty string and string containing just "."
			// Remove duplicate spaces and append "."
			List<string> sentences = page.Split('.').Select(s => s.Trim()).Where(s => !String.IsNullOrEmpty(s) && (s != ".")).Select(s => Regex.Replace(s, " +"," ") + ".").ToList();

			return sentences;
		}

		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main()
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Program program = new Program();
			program.Initialize();
		}
	}
}
