﻿using System;
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

using ForceDirectedGraph;

using Clifton.ExtensionMethods;
using Clifton.MycroParser;
using Clifton.Tools.Strings.Extensions;

namespace NlpVisualizer
{
	/// <summary>
	/// An extension method that appends text with a specific color to a RichTextBox.
	/// </summary>
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

	/// <summary>
	/// Internal structure for tracking a keyword, its containing index, and its relevance.
	/// </summary>
	public class SentenceInfo
	{
		public string Keyword { get; set; }
		public int Index { get; set; }
		public double Relevance { get; set; }
	}

	public class Program
	{
		const int FDG_DEPTH_LIMIT = 3;
		const int FDG_NODE_KEYWORD_LIMIT = 5;

		protected Form form;
		protected TextBox tbUrl;
		protected DataGridView dgvKeywords;
		protected StatusBarPanel sbStatus;
		protected Button btnProcess;
		protected Label lblAlchemyKeywords;
		protected RichTextBox rtbSentences;
		protected Button btnPrevSentence;
		protected Button btnNextSentence;
		protected RadioButton rbNeighboringSentenceKeywords;
		protected RadioButton rbKeywordDirectedGraph;
		protected Surface surface;
		
		protected AlchemyWrapper alchemy;
		protected DataSet dsKeywords;
		protected DataView dvKeywords;
		protected string pageText;
		protected List<string> pageSentences;
		protected List<int> displayedSentenceIndices;
		protected int currentSentenceIdx;
		protected bool textboxEventsEnabled;
		protected string keyword;

		// Maps the relevance of each keyword.
		public Dictionary<string, double> keywordRelevanceMap;

		// Keywords, sorted by relevance.
		public SortedList<double, string> keywordByRelevanceList;

		// Keywords in each sentence, by index.
		public Dictionary<int, List<string>> sentenceKeywordMap;

		// Sentence indices for each keyword occurrance.
		public Dictionary<string, List<int>> keywordSentenceMap;

		public double minRelevance;
		public double maxRelevance;
		public bool directedGraph;
		public Diagram mDiagram;

		public static Program app;

		public Program()
		{
			app = this;
			displayedSentenceIndices = new List<int>();
			keywordRelevanceMap = new Dictionary<string, double>();
			sentenceKeywordMap = new Dictionary<int, List<string>>();
			keywordSentenceMap = new Dictionary<string, List<int>>();
			keywordByRelevanceList = new SortedList<double, string>();
			mDiagram = new Diagram();
		}

		/// <summary>
		/// Set up the UI and initialize the NLP.
		/// </summary>
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
			surface = (Surface)mp.ObjectCollection["surface"];

			rbNeighboringSentenceKeywords = (RadioButton)mp.ObjectCollection["rbNeighboringSentenceKeywords"];
			rbKeywordDirectedGraph = (RadioButton)mp.ObjectCollection["rbKeywordDirectedGraph"];
				
			InitializeNlp();

			try
			{
				Application.Run(form);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine(ex.Message);
			}
		}

		/// <summary>
		/// When a keyword is selected from the grid or the visualizator, we update RTB
		/// to display the sentences containing the keyword and also the keyword relationship visualization.
		/// </summary>
		public void ShowKeywordSelection(string keyword)
		{
			textboxEventsEnabled = false;
			ShowSentences(keyword);
			textboxEventsEnabled = true;
			rtbSentences.SelectionStart = 0;
			surface.NewKeyword(keyword);
			UpdateKeywordVisualization();
		}

		public void SelectKeyword(string keyword)
		{
			int idx = 0;

			foreach(DataGridViewRow row in dgvKeywords.Rows)
			{
				row.Selected = row.Cells[0].Value.ToString() == keyword;

				if (row.Selected)
				{
					break;
				}

				++idx;
			}

			dgvKeywords.FirstDisplayedScrollingRowIndex = idx;
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

		/// <summary>
		/// Analyze the document, extracting the text the keywords, and create the keyword-sentence maps.
		/// </summary>
		protected async void Process(object sender, EventArgs args)
		{
			btnProcess.Enabled = false;
			ClearAllGrids();
			string url = tbUrl.Text;
			sbStatus.Text = "Acquiring page content...";
			try
			{
				pageText = await Task.Run(() => GetUrlText(url));

				pageSentences = ParseOutSentences(pageText);
				sbStatus.Text = "Acquiring keywords from AlchemyAPI...";

				dsKeywords = await Task.Run(() => GetKeywords(url, pageText));

				sbStatus.Text = "Processing results...";
				dvKeywords = new DataView(dsKeywords.Tables["keyword"]);
				StripEndingPeriods();
				CreateSortedKeywordList(dvKeywords);
				CreateSentenceKeywordMaps(dvKeywords);
				CreateKeywordRelevanceMap(dvKeywords);		// Must be done before assigning the data source.
				sbStatus.Text = "Ready";
				dgvKeywords.DataSource = dvKeywords;
				lblAlchemyKeywords.Text = String.Format("Keywords: {0}", dvKeywords.Count);
				btnProcess.Enabled = true;
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message, "Processing Error", MessageBoxButtons.OK);
			}
			finally
			{
				sbStatus.Text = "Ready";
				btnProcess.Enabled = true;
			}
		}

		/// <summary>
		/// ALCHEMYAPI
		/// For some reason, some of they keywords coming back from AlchemyAPI have an ending period which we need to strip off.
		/// </summary>
		protected void StripEndingPeriods()
		{
			dvKeywords.ForEach(row =>
				{
					string keyword = row[0].ToString();

					if (keyword.EndsWith("."))
					{
						row[0] = keyword.Substring(0, keyword.Length - 1);
					}
				});
		}

		protected void CreateSortedKeywordList(DataView dvKeywords)
		{
			keywordByRelevanceList.Clear();

			dvKeywords.ForEach(row =>
				{
					double relevance = Convert.ToDouble(row[1].ToString());
					keywordByRelevanceList[relevance] = row[0].ToString();
				});
		}

		/// <summary>
		/// Create the sentence-keyword map (list of keywords in each sentence.)
		/// Create the keyword-sentence map (list of sentence indices for each keyword.)
		/// </summary>
		/// <param name="dvKeywords"></param>
		protected void CreateSentenceKeywordMaps(DataView dvKeywords)
		{
			sentenceKeywordMap.Clear();
			keywordSentenceMap.Clear();

			// For each sentence, get all the keywords in that sentence.
			pageSentences.ForEachWithIndex((s, idx) =>
				{
					List<string> keywordsInSentence = new List<string>();
					sentenceKeywordMap[idx] = keywordsInSentence;
					string sl = s.ToLower();

					// For each of the returned keywords in the view...
					dvKeywords.ForEach(row =>
						{
							string keyword = row[0].ToString();

							if (sl.Contains(keyword.ToLower()))
							{
								// Add keyword to sentence-keyword map.
								keywordsInSentence.Add(keyword);

								// Add sentence to keyword-sentence map.
								List<int> sentences;

								if (!keywordSentenceMap.TryGetValue(keyword, out sentences))
								{
									// No entry for this keyword yet, so create the sentence indices list.
									sentences = new List<int>();
									keywordSentenceMap[keyword] = sentences;
								}

								sentences.AddIfUnique(idx);
							}
						});
				});
		}

		protected void CreateKeywordRelevanceMap(DataView dvKeywords)
		{
			minRelevance = 1;
			maxRelevance = 0;

			keywordRelevanceMap.Clear();

			dvKeywords.ForEach(row =>
				{
					double relevance = Convert.ToDouble(row[1].ToString());
					keywordRelevanceMap[row[0].ToString()] = relevance;

					(relevance < minRelevance).Then(() => minRelevance = relevance);
					(relevance > maxRelevance).Then(() => maxRelevance = relevance);
				});
		}

		protected void OnKeywordSelection(object sender, EventArgs args)
		{
			DataGridViewSelectedRowCollection rows = dgvKeywords.SelectedRows;

			if (rows.Count > 0)
			{
				// We only allow one row to be selected.
				DataGridViewRow row = rows[0];
				keyword = row.Cells[0].Value.ToString();
				ShowKeywordSelection(keyword);
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

		protected void OnSelectSentence(object sender, EventArgs args)
		{
			ShowSentence(currentSentenceIdx);
		}

		protected void GraphModeChanged(object sender, EventArgs args)
		{
			if (directedGraph != rbKeywordDirectedGraph.Checked)
			{
				directedGraph = rbKeywordDirectedGraph.Checked;
				UpdateKeywordVisualization();
				surface.Invalidate(true);
			}
		}

		protected void ShowSentence(int idx)
		{
			textboxEventsEnabled = false;
			string sentence = pageSentences[idx];
			ShowSentenceWithKeywords(sentence, keyword);
			btnPrevSentence.Enabled = idx > 0;
			btnNextSentence.Enabled = idx < pageSentences.Count - 1;

			// Clear the displayed sentence index list and add only the sentence we are currently displaying.
			displayedSentenceIndices.Clear();
			displayedSentenceIndices.Add(idx);

			textboxEventsEnabled = true;
			rtbSentences.SelectionStart = 0;

			if (sentence.ToLower().Contains(keyword.ToLower()))
			{
				surface.NewKeyword(keyword);
			}
			else
			{
				// The currently selected keyword is not contained in this sentence.
				surface.NewKeyword("[ ]");
			}

			UpdateKeywordVisualization();
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

		/// <summary>
		/// Build the text, checking for keyword occurrence and if found, colorizing the keyword.
		/// </summary>
		protected void ShowSentences(string keyword)
		{
			rtbSentences.Clear();
			displayedSentenceIndices.Clear();

			pageSentences.ForEachWithIndex((sentence, sidx) =>
			{
				string s = sentence.ToLower();
				int idx = s.IndexOf(keyword.ToLower());
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
					idx = s.IndexOf(keyword.ToLower());
				}

				if (found)
				{
					// Append the remainder.
					rtbSentences.AppendText(s);
					rtbSentences.AppendText("\n\n");
				}
			});
		}

		protected List<SentenceInfo> GetPreviousSentencesKeywords()
		{
			List<SentenceInfo> ret = new List<SentenceInfo>();

			displayedSentenceIndices.ForEach(dsi =>
				{
					if (dsi > 0)
					{
						ret.AddRange(GetKeywordsInSentence(dsi - 1));
					}
				});

			return ret;
		}

		public List<SentenceInfo> GetSentencesKeywords()
		{
			List<SentenceInfo> ret = new List<SentenceInfo>();

			displayedSentenceIndices.ForEach(dsi =>
			{
				ret.AddRange(GetKeywordsInSentence(dsi));
			});

			return ret;
		}

		protected List<SentenceInfo> GetNextSentencesKeywords()
		{
			List<SentenceInfo> ret = new List<SentenceInfo>();

			displayedSentenceIndices.ForEach(dsi =>
			{
				if (dsi < pageSentences.Count - 1)
				{
					ret.AddRange(GetKeywordsInSentence(dsi + 1));
				}
			});

			return ret;
		}

		protected List<SentenceInfo> GetKeywordsInSentence(int idx)
		{
			List<SentenceInfo> ret = new List<SentenceInfo>();
			sentenceKeywordMap[idx].ForEach(k => ret.Add(new SentenceInfo() { Keyword = k, Index = idx, Relevance = keywordRelevanceMap[k] }));

			return ret;
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

		protected void UpdateKeywordVisualization()
		{
			List<SentenceInfo> prevKeywords = GetPreviousSentencesKeywords();
			List<SentenceInfo> nextKeywords = GetNextSentencesKeywords();
			surface.PreviousKeywords(prevKeywords);
			surface.NextKeywords(nextKeywords);

			if (directedGraph)
			{
				UpdateDirectedGraph();
			}
	
			surface.Invalidate(true);
		}

		protected List<string> parsedKeywords = new List<string>();

		protected void UpdateDirectedGraph()
		{
			mDiagram.Clear();
			parsedKeywords.Clear();

			string ctrSentence =  FirstThreeWords(pageSentences[displayedSentenceIndices[0]]);
			Node node = new TextNode(surface, ctrSentence);
			((TextNode)node).Brush = surface.greenBrush;
			mDiagram.AddNode(node);

			// Get the keywords of all sentences for the current sentence or sentences containing the selected keyword.
			List<SentenceInfo> keywords = GetSentencesKeywords();
			keywords = keywords.RemoveDuplicates((si1, si2) => si1.Keyword.ToLower() == si2.Keyword.ToLower()).ToList();
			parsedKeywords.AddRange(keywords.Select(si => si.Keyword.ToLower()));
			AddKeywordsToGraphNode(node, keywords, 0);
			mDiagram.Arrange();
		}

		protected void AddKeywordsToGraphNode(Node node, List<SentenceInfo> keywords, int depth)
		{
			if (depth < FDG_DEPTH_LIMIT)
			{
				int idx = 0;

				foreach(SentenceInfo si in keywords)
				{
					// Limit # of keywords we display.
					if (idx++ < FDG_NODE_KEYWORD_LIMIT)
					{
						Node child = new TextNode(surface, si.Keyword);
						node.AddChild(child);

						// Get all sentences indices containing this keyword:
						List<int> containingSentences = keywordSentenceMap[si.Keyword];

						// Now get the related keywords for each of those sentences.  
						List<SentenceInfo> relatedKeywords = new List<SentenceInfo>();

						containingSentences.ForEach(cs =>
							{
								// Get the unique and previously not processed keywords in the sentence.
								List<SentenceInfo> si3 = GetKeywordsInSentence(cs).Where(sik => !parsedKeywords.Contains(sik.Keyword.ToLower())).ToList();
								// TODO: sort by relevance
								si3 = si3.RemoveDuplicates((si1, si2) => si1.Keyword.ToLower() == si2.Keyword.ToLower()).ToList();
								relatedKeywords.AddRange(si3);
								parsedKeywords.AddRange(si3.Select(sik=>sik.Keyword.ToLower()));
							});

						if (relatedKeywords.Count > 0)
						{
							AddKeywordsToGraphNode(child, relatedKeywords, depth + 1);
						}
					}
					else
					{
						break;
					}
				}
			}
		}

		protected string FirstThreeWords(string s)
		{
			string ret = s.LeftOf(" ") + " " + s.RightOf(" ").LeftOf(" ") + " " + s.RightOf(" ").RightOf(" ").LeftOf(" ");
			ret = ret + "...";

			return ret;
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
