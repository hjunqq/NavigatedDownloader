using iTextSharp.text;
using iTextSharp.text.pdf;
using Jurassic;
using mshtml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Forms;
using System.Timers;
using System.Xml.Linq;
using System.Threading.Tasks;

namespace NavigatedDownloader
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public static class ListExtensions
    {
        public static List<List<T>> ChunkBy<T>(this List<T> source, int chunkSize)
        {
            return source
                .Select((x, i) => new { Index = i, Value = x })
                .GroupBy(x => x.Index / chunkSize)
                .Select(x => x.Select(v => v.Value).ToList())
                .ToList();
        }
    }
    public class BrowserHelper
    {
        private const int INTERNET_COOKIE_HTTPONLY = 0x00002000;

        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetGetCookieEx(
            string url,
            string cookieName,
            StringBuilder cookieData,
            ref int size,
            int flags,
            IntPtr pReserved);

        public static string GetCookie(string url)
        {
            int size = 512;
            StringBuilder sb = new StringBuilder(size);
            if (!InternetGetCookieEx(url, null, sb, ref size, INTERNET_COOKIE_HTTPONLY, IntPtr.Zero))
            {
                if (size < 0)
                {
                    return null;
                }
                sb = new StringBuilder(size);
                if (!InternetGetCookieEx(url, null, sb, ref size, INTERNET_COOKIE_HTTPONLY, IntPtr.Zero))
                {
                    return null;
                }
            }
            return sb.ToString();
        }
    }
    public class DownloadParameter
    {
        public int tid { get; set; }
        public string storagepath { get; set; }
        public List<string[]> filenameList { get; set; }
        public ManualResetEvent tStartEvent { get; set; }
        public ManualResetEvent tDoneEvent { get; set; }
        public ManualResetEvent tBeginBlock { get; set; }
        public ManualResetEvent tEndBlock { get; set; }
        public string domain { get; set; }
        public string imgBaseURL { get; set; }
        public string cookie { get; set; }
        public string menuValue { get; set; }

        
    }
    public class MenuParameter
    {
        public int[] pages { get; set; }
        public string[] captions { get; set; }
        public int nbookmarks { get; set; }
    }

    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            SHDocVw.IWebBrowser2 axBrowser = typeof(System.Windows.Controls.WebBrowser).GetProperty("AxIWebBrowser2", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(browser, null) as SHDocVw.IWebBrowser2;
            axBrowser.Silent = true;
            ((SHDocVw.DWebBrowserEvents_Event)axBrowser).NewWindow += OnWebBrowserNewWindow;

            regexStrings = File.ReadAllLines("RegEx.txt").Select(i => i.Trim()).Where(i => i != string.Empty).ToArray();
        }

        private string[] regexStrings;
        //private AutoResetEvent waitEvent = new AutoResetEvent(true);
        private AutoResetEvent[] waitEvent;
        Thread[] threads;

        private string code;
        private bool isUiEnabled;
        private int nThreads = 2;
        private System.Timers.Timer timer = new System.Timers.Timer();
        private int elapsedTimes;
        private string title;
        string storePath = string.Empty;
        private MenuParameter menu;
        private void OnWebBrowserNewWindow(string URL, int Flags, string TargetFrameName, ref object PostData, string Headers, ref bool Processed)
        {
            Processed = true;
            browser.Navigate(URL);
        }

        private void goButton_Click(object sender, RoutedEventArgs e)
        {
            browser.Navigate(this.urlTextBox.Text);
            this.goButton.IsDefault = false;
        }

        private void browser_LoadCompleted(object sender, NavigationEventArgs e)
        {
            string html = string.Empty;
            HTMLDocument doc = browser.Document as HTMLDocument;
            if (doc != null)
            {
                html = doc.documentElement.innerHTML;
            }
            if (Regex.IsMatch(html, regexStrings[0]) == true)
            {
                this.downloadButton.IsEnabled = true;
                this.numericUpDown.IsEnabled = true;
            }
        }

        private void browser_Navigating(object sender, NavigatingCancelEventArgs e)
        {
            this.urlTextBox.Text = e.Uri.AbsoluteUri;
            this.downloadButton.IsEnabled = false;
            this.numericUpDown.IsEnabled = false;
            this.elapsedTimeBox.Text = string.Empty;
        }

        private void downloadButton_Click(object sender, RoutedEventArgs e)
        {
            HTMLDocument doc = browser.Document as HTMLDocument;
            string domain = Regex.Match(browser.Source.AbsoluteUri, "^http://.*?(?=/)").Value;
            string html = doc.documentElement.innerHTML;
            string cookie = BrowserHelper.GetCookie(browser.Source.AbsoluteUri);

            nThreads = (int)numericUpDown.Value;

            title = Regex.Match(html, "(?<=<TITLE>).*(?=</TITLE>)").Value;
            this.bookname.Text = title;

            this.downloadButton.IsEnabled = false;
            this.pdfButton.IsEnabled = false;
            this.urlTextBox.IsEnabled = false;
            //this.urlTextBox.Text = string.Empty;
            this.goButton.IsEnabled = false;
            this.browser.IsEnabled = false;
            this.numericUpDown.IsEnabled = false;
            //this.browser.Navigate("about:blank");

            storePath = pathBox.Text;

            timer.Interval = 1000;
            timer.Elapsed += new ElapsedEventHandler(OnTimedEvent);
            timer.Start();

            string[] workObject = new string[] { domain, html, cookie };
            Thread tread = new Thread(DownLoadBook);
            tread.Start(workObject);
        }

        private void OnTimedEvent(object source, ElapsedEventArgs e)
        {
            elapsedTimes++;
            this.Dispatcher.BeginInvoke(new Action<int>(SetElapsedTime), elapsedTimes);
            Console.Write(elapsedTimes.ToString() + "\n");
        }
        private void SetElapsedTime(int elapsedTimes)
        {
            int minites = 0;
            int seconds = 0;
            seconds = elapsedTimes % 60;
            minites = elapsedTimes / 60;
            this.elapsedTimeBox.Text = minites.ToString("D2") + ":" + seconds.ToString("D2");
        }
        private void DownLoadBook(object obj)
        {
            string[] workObject = (string[])obj;
            string domain = workObject[0];
            string html = workObject[1];
            string cookie = workObject[2];

            string title = Regex.Match(html, "(?<=<TITLE>).*(?=</TITLE>)").Value;


            ScriptEngine engine = new ScriptEngine();

            if (Directory.Exists(storePath) == false)
            {
                Directory.CreateDirectory(storePath);
            }

            string dlpath = storePath + "\\" + title;
            if (Directory.Exists(dlpath) == false)
            {
                Directory.CreateDirectory(dlpath);
            }

            List<string[]> fileNameList = new List<string[]>();

            string jsPageDef = Regex.Match(html, regexStrings[1]).Value;
            string jsPages = Regex.Match(html, regexStrings[3]).Value;

            dynamic pages = engine.Evaluate(jsPageDef + jsPages);
            for (int k = 0; k < pages.Length && k < 8; k++)
            {
                int begin = 0;
                int end = 0;
                if (pages[k].Length != 2 || int.TryParse(pages[k][0].ToString(), out begin) == false || int.TryParse(pages[k][1].ToString(), out end) == false)
                {
                    continue;
                }
                if (begin > end)
                {
                    continue;
                }

                string pageNamePrefix = null;
                string formatPattern = null;
                string fileNamePrefix = null;
                switch (k)
                {
                    case 0:
                        pageNamePrefix = "cov";
                        formatPattern = "D3";
                        fileNamePrefix = "A-";
                        break;
                    case 1:
                        pageNamePrefix = "bok";
                        formatPattern = "D3";
                        fileNamePrefix = "B-";
                        break;
                    case 2:
                        pageNamePrefix = "leg";
                        formatPattern = "D3";
                        fileNamePrefix = "C-";
                        break;
                    case 3:
                        pageNamePrefix = "fow";
                        formatPattern = "D3";
                        fileNamePrefix = "D-";
                        break;
                    case 4:
                        pageNamePrefix = "!";
                        formatPattern = "D5";
                        fileNamePrefix = "E-";
                        break;
                    case 5:
                        pageNamePrefix = "";
                        formatPattern = "D6";
                        fileNamePrefix = "F-";
                        break;
                    case 6:
                        pageNamePrefix = "att";
                        formatPattern = "D3";
                        fileNamePrefix = "G-";
                        break;
                    case 7:
                        pageNamePrefix = "cov";
                        formatPattern = "D3";
                        fileNamePrefix = "H-";
                        break;
                }

                for (int j = begin; j <= end; j++)
                {
                    fileNameList.Add(new string[] { pageNamePrefix + j.ToString(formatPattern), fileNamePrefix + j.ToString() });
                }
            }

            string imgBaseURL = Regex.Match(html, regexStrings[2]).Value;

            string menuValue = Regex.Match(html, regexStrings[4]).Value.Replace("&amp;","&");


            //download menu
            DownloadParameter downloadMenu = new DownloadParameter();

            downloadMenu.cookie = cookie;
            downloadMenu.menuValue = menuValue;

            Task<MenuParameter> doGetMenu = Task.Factory.StartNew<MenuParameter>(() => GetMenu(downloadMenu));
            menu = doGetMenu.Result;


            int i = 0, n = fileNameList.Count;
            int[] progress = new int[] { i, n };
            //nThreads = 5;
            ManualResetEvent[] downloadStartEvents = new ManualResetEvent[nThreads];
            ManualResetEvent[] downloadDoneEvents = new ManualResetEvent[nThreads];
            threads = new Thread[nThreads];
            DownloadParameter[] downloadParams = new DownloadParameter[nThreads];
            waitEvent = new AutoResetEvent[nThreads];


            List<List<string[]>> subFileNameList = fileNameList.ChunkBy(n / nThreads + 1);

            for (int ithread = 0; ithread < nThreads; ithread++)
            {
                downloadStartEvents[ithread] = new ManualResetEvent(false);
                downloadDoneEvents[ithread] = new ManualResetEvent(false);
                waitEvent[ithread] = new AutoResetEvent(true);

                downloadParams[ithread] = new DownloadParameter();
                downloadParams[ithread].tid = ithread;
                downloadParams[ithread].filenameList = subFileNameList[ithread];
                downloadParams[ithread].storagepath = dlpath;
                downloadParams[ithread].domain = domain;
                downloadParams[ithread].imgBaseURL = imgBaseURL;
                downloadParams[ithread].cookie = cookie;
                downloadParams[ithread].tDoneEvent = downloadDoneEvents[ithread];
                downloadParams[ithread].menuValue = menuValue;
                threads[ithread] = new Thread(MultiDownload);
                threads[ithread].Start(downloadParams[ithread]);
            }

            this.Dispatcher.BeginInvoke(new Action<object>(ShowProgress), progress);

            WaitHandle.WaitAll(downloadDoneEvents, -1);

            //foreach (var fileName in fileNameList)
            //{
            //    string imgURL = domain + imgBaseURL + fileName[0] + "?.&uf=ssr&zoom=2";
            //    MemoryStream ms = GetResponse(imgURL, cookie);
            //    File.WriteAllBytes(string.Format(dlpath + @"\{0}.jpg", fileName[1]), ms.ToArray());
            //    progress[0]++;
            //    this.Dispatcher.BeginInvoke(new Action<object>(ShowProgress), progress);
            //}

            this.Dispatcher.BeginInvoke(new Action<string>(ShowDone), "下载完成！");
        }
        private MenuParameter GetMenu(object dParams)
        {
            DownloadParameter downParams = (DownloadParameter)dParams;
            string menuBaseUrl = "http://path.sslibrary.com/cat/cat2xml.dll?";
            string meunValue = downParams.menuValue;
            string menuUrl = menuBaseUrl + meunValue;

            HttpWebRequest request = WebRequest.Create(menuUrl) as HttpWebRequest;
            request.Headers.Add("Cookie", downParams.cookie);
            request.Method = "GET";
            request.UserAgent = "Mozilla/5.0 (Windows NT 6.1; WOW64; Trident/7.0; rv:11.0) like Gecko";
            HttpWebResponse response = request.GetResponse() as HttpWebResponse;


            MemoryStream ms = new MemoryStream();
            response.GetResponseStream().CopyTo(ms);

            MenuParameter mpara = new MenuParameter();

            if(response.ContentType.Contains("text/html") == true)
            {
                ms.Seek(0, SeekOrigin.Begin);
                //string xml = new StreamReader(ms).ReadToEnd();
                XElement xe = XElement.Load(ms);
                IEnumerable<XElement> elements = from ele in xe.Elements("tree")
                                                 select ele;
                string[] captions = new string[elements.Count()];
                int[] pages = new int[elements.Count()];
                int i=0;
                foreach(XElement ele in elements)
                {
                    captions[i] = ele.FirstAttribute.NextAttribute.Value;
                    pages[i] = int.Parse(ele.FirstAttribute.NextAttribute.NextAttribute.Value);
                    i += 1;
                }

                mpara.pages = pages;
                mpara.captions = captions;
                mpara.nbookmarks = elements.Count();
            }

            return mpara;
        }
        private void MultiDownload(object dParams)
        {
            DownloadParameter downParams = (DownloadParameter)dParams;

            int i = 0;
            foreach (var fileName in downParams.filenameList)
            {
                Console.Write(fileName[1] + "  " + downParams.tid.ToString() + "\n");
                if (isUiEnabled == false)
                {
                    waitEvent[downParams.tid].Reset();
                }
                else
                {
                    waitEvent[downParams.tid].WaitOne();
                }
                if (!File.Exists(string.Format(downParams.storagepath + @"\{0}.jpg", fileName[1])))
                {
                    string imgURL = downParams.domain + downParams.imgBaseURL + fileName[0] + "?.&uf=ssr&zoom=2";
                    MemoryStream ms = GetResponse(imgURL, downParams.cookie, downParams.tid);
                    File.WriteAllBytes(string.Format(downParams.storagepath + @"\{0}.jpg", fileName[1]), ms.ToArray());
                }
                this.Dispatcher.BeginInvoke(new Action<int>(AddProgress), i++);
            }

            downParams.tDoneEvent.Set();

        }
        private void Reset()
        {
            this.urlTextBox.IsEnabled = true;
            //this.urlTextBox.Text = string.Empty;
            this.goButton.IsEnabled = true;
            this.browser.IsEnabled = true;
            //this.browser.Navigate("http://www.duxiu.com");
            this.downloadButton.IsEnabled = false;
            this.pdfButton.IsEnabled = true;
            this.codeImage.Source = null;
            this.codeTextBox.Text = string.Empty;
            this.codeTextBox.IsEnabled = false;
            this.codeButton.IsEnabled = false;
        }

        private void ShowDone(string str)
        {
            System.Windows.MessageBox.Show(str);
            Reset();
            timer.Stop();
            elapsedTimes = 0;
        }

        private void EnableUI(Stream stream)
        {
            isUiEnabled = true;
            BitmapImage image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = stream;
            image.EndInit();

            this.codeImage.Source = image;

            System.Guid guid = new Guid();
            guid = Guid.NewGuid();
            string str = guid.ToString();
            FileStream fm = new FileStream(@"capchar\" + str + ".jpg", FileMode.Create);
            JpegBitmapEncoder encoder = new JpegBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(fm);
            fm.Close();

            this.codeTextBox.IsEnabled = true;
            this.codeTextBox.Focus();
            this.codeTextBox.Text = string.Empty;
            this.codeButton.IsEnabled = true;
            this.codeButton.IsDefault = true;
            this.cancelButton.IsEnabled = false;
        }

        private void DisableUI()
        {
            isUiEnabled = false;
            this.codeImage.Source = null;
            this.codeTextBox.Text = string.Empty;
            this.codeTextBox.IsEnabled = false;
            this.codeButton.IsEnabled = false;
            this.cancelButton.IsEnabled = true;
        }


        private void codeButton_Click(object sender, RoutedEventArgs e)
        {
            code = this.codeTextBox.Text;
            DisableUI();
            for (int i = 0; i < nThreads; i++)
            {
                waitEvent[i].Set();
            }
        }

        private void ShowError(string str)
        {
            System.Windows.MessageBox.Show(str, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Reset();
        }

        private void ShowProgress(object progress)
        {
            int[] prog = (int[])progress;
            int i = prog[0];
            int n = prog[1];
            progressBar.Maximum = n;
            progressBar.Value = i;
            progressi.Text = i.ToString();
            progressn.Text = n.ToString();
        }
        private void AddProgress(int i)
        {
            progressBar.Value++;
            progressi.Text = (int.Parse(progressi.Text) + 1).ToString();
        }
        private MemoryStream GetResponse(string url, string cookie, int tid)
        {
            while (true)
            {
                try
                {
                    HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
                    request.Headers.Add("Cookie", cookie);
                    request.Method = "GET";
                    request.UserAgent = "Mozilla/5.0 (Windows NT 6.1; WOW64; Trident/7.0; rv:11.0) like Gecko";
                    HttpWebResponse response = request.GetResponse() as HttpWebResponse;

                    MemoryStream ms = new MemoryStream();
                    response.GetResponseStream().CopyTo(ms);

                    if (response.ContentType.Contains("text/html") == true)
                    {
                        ms.Seek(0, SeekOrigin.Begin);
                        string html = new StreamReader(ms).ReadToEnd();
                        if (html.Contains("/processVerifyPng.ac") == true)
                        {
                            string domainName = Regex.Match(url, "^http://.*?(?=/)").Value;
                            string codeImageURL = domainName + "/n/n/processVerifyPng.ac";
                            request = WebRequest.Create(codeImageURL) as HttpWebRequest;
                            request.Headers.Add("Cookie", cookie);
                            request.Method = "GET";
                            request.UserAgent = "Mozilla/5.0 (Windows NT 6.1; WOW64; Trident/7.0; rv:11.0) like Gecko";
                            response = request.GetResponse() as HttpWebResponse;

                            ms = new MemoryStream();
                            response.GetResponseStream().CopyTo(ms);

                            waitEvent[tid].Reset();
                            this.Dispatcher.BeginInvoke(new Action<Stream>(EnableUI), ms);
                            waitEvent[tid].WaitOne();

                            string commitCodeString = domainName + "/n/processVerify.ac?ucode=" + code;
                            request = WebRequest.Create(commitCodeString) as HttpWebRequest;
                            request.Headers.Add("Cookie", cookie);
                            request.Method = "GET";
                            request.UserAgent = "Mozilla/5.0 (Windows NT 6.1; WOW64; Trident/7.0; rv:11.0) like Gecko";
                            response = request.GetResponse() as HttpWebResponse;
                            response.Close();

                            continue;
                        }
                    }

                    ms.Seek(0, SeekOrigin.Begin);
                    return ms;
                }
                catch (Exception ex)
                {
                    WebException wex = ex as WebException;
                    wex = null;
                    if (wex != null && (wex.Response as HttpWebResponse).StatusCode == HttpStatusCode.Forbidden)
                    {
                        this.Dispatcher.BeginInvoke(new Action<string>(ShowError), "Forbidden! \r\n" + url);
                        Thread.CurrentThread.Abort();
                    }
                }
            }
        }

        private void pdfButton_Click(object sender, RoutedEventArgs e)
        {
            Thread tread = new Thread(GenPDF);
            tread.Start(title);
            storePath = pathBox.Text;
        }

        private void GenPDF(object title)
        {
            IEnumerable<FileInfo> images = null;
            if (Directory.Exists(storePath) == true)
            {
                var directories = Directory.GetDirectories(storePath);
                DirectoryInfo dirInfo;
                foreach (string dir in directories)
                {
                    dirInfo = new DirectoryInfo(dir);
                    if (title != null)
                    {
                        if (dirInfo.Name.ToString() != (string)title)
                        {
                            continue;
                        }
                    }

                    images = dirInfo.EnumerateFiles("*.jpg").OrderBy(i => i.Name[0]).ThenBy(i => i.Name.Length).ThenBy(i => i.Name);

                    if (images != null && images.Count() > 0)
                    {
                        Document pdfDoc = new Document();
                        pdfDoc.SetMargins(0, 0, 0, 0);
                        PdfWriter pdfWriter = PdfWriter.GetInstance(pdfDoc, new FileStream(dir + @".pdf", FileMode.Create));
                        pdfDoc.Open();
                        int mainPageStartNumber = 0;
                        bool entryMainPage = false;

                        int i = 0, n = images.Count();
                        int[] progress = new int[] { i, n };
                        foreach (var image in images)
                        {
                            if (entryMainPage == false)
                            {
                                mainPageStartNumber++;
                                if (image.Name[0] == 'F')
                                {
                                    entryMainPage = true;
                                }
                            }
                            iTextSharp.text.Image pageImage = iTextSharp.text.Image.GetInstance(image.FullName);
                            pdfDoc.SetPageSize(new Rectangle(pageImage.Width, pageImage.Height));
                            pdfDoc.NewPage();
                            pdfDoc.Add(pageImage);
                            progress[0]++;
                            this.Dispatcher.BeginInvoke(new Action<object>(ShowProgress), progress);
                        }
                        PdfPageLabels labels = new PdfPageLabels();
                        labels.AddPageLabel(1, PdfPageLabels.LOWERCASE_ROMAN_NUMERALS);
                        labels.AddPageLabel(mainPageStartNumber, PdfPageLabels.DECIMAL_ARABIC_NUMERALS);
                        pdfWriter.PageLabels = labels;

                        string caption;
                        int ipage;
                        PdfContentByte cb = pdfWriter.DirectContent;
                        PdfOutline root = cb.RootOutline;

                        for(int ibkm=0; ibkm < menu.nbookmarks; ibkm++)
                        {
                            caption = menu.captions[ibkm];
                            ipage = menu.pages[ibkm];
                            PdfAction action = PdfAction.GotoLocalPage(ipage, new PdfDestination(PdfDestination.FIT), pdfWriter);
                            PdfOutline outline = new PdfOutline(root, action, caption);
                        }


                        pdfDoc.Close();
                        this.Dispatcher.BeginInvoke(new Action<string>(ShowDone), "已生成PDF");

                    }
                    else
                    {
                        this.Dispatcher.BeginInvoke(new Action<string>(ShowError), "No Picture!");
                    }
                }

            }

        }
        private void Window_Closed(object sender, EventArgs e)
        {
            Environment.Exit(0);
        }

        private void browserButton_Click(object sender, RoutedEventArgs e)
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            dialog.SelectedPath = Environment.CurrentDirectory + "\\Download";
            DialogResult result = dialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                pathBox.Text = dialog.SelectedPath;
            }

        }

        private void cancelButton_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < nThreads; i++)
            {
                threads[i].Abort();
            }
        }
    }
}
