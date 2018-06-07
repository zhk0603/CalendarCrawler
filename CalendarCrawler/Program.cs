using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Crawler;
using Crawler.Pipelines;
using Crawler.Schedulers;
using HtmlAgilityPack;

namespace CalendarCrawler
{
    class Program
    {
        static void Main(string[] args)
        {
            var beginYear = int.Parse(System.Configuration.ConfigurationManager.AppSettings["beginYear"]);
            var endYear = int.Parse(System.Configuration.ConfigurationManager.AppSettings["endYear"]);

            var opt = new ClandarPipelineOptions
            {
                Name = nameof(ClandarPipeline),
                BeginYear = beginYear,
                EndYear = endYear,
                Sleep = 1000
            };

            var crawler = CrawlerBuilder.Current
                .UsePipeline<ClandarPipeline>(opt)
                .UsePipeline<BuilderJsPipeline>(new PipelineOptions {Name = nameof(BuilderJsPipeline)})
                .UseMultiThread(1)
                .UseNamed("黄历爬虫")
                .Builder();

            crawler.Run();

            Console.ReadKey();
        }
    }

    /// <summary>
    /// 抓取  https://wannianrili.51240.com/ 中的黄历信息。
    /// </summary>
    public class ClandarPipeline : CrawlerPipeline<ClandarPipelineOptions>
    {
        private const string Api = "https://wannianrili.51240.com/ajax/?q={0}&v=17052214";

        private readonly IScheduler _huangliScheduler;

        public ClandarPipeline(ClandarPipelineOptions options) : base(options)
        {
            Options.Scheduler = SchedulerManager.GetScheduler<Scheduler<string>>("urlScheduler");
            _huangliScheduler = SchedulerManager.GetScheduler<Scheduler<HuangliMonth>>("huangliScheduler");
        }

        protected override void Initialize(PipelineContext context)
        {
            for (var i = Options.BeginYear; i <= Options.EndYear; i++)
            {
                for (var j = 1; j < 13; j++)
                {
                    Options.Scheduler.Push(string.Format(Api, $"{i}-{j.ToString().PadLeft(2, '0')}"));
                }
            }
        }

        protected override Task<bool> ExecuteAsync(PipelineContext context)
        {
            if (context.Site != null)
            {
                var requestSite = context.Site;
                requestSite.Referer = "https://wannianrili.51240.com/";
                requestSite.Accept = "*/*";

                var responsePage = Options.Downloader.GetPage(requestSite);
                if (responsePage.HttpStatusCode == 200)
                {
                    var allDayList = responsePage.DocumentNode.SelectNodes("//div[@class=\"wnrl_k_you\"]");
                    if (allDayList != null && allDayList.Count > 0)
                    {
                        var month = new HuangliMonth();

                        AnalysisHelper.AnalysisMonth(month, responsePage);

                        foreach (var item in allDayList)
                        {
                            var day = AnalysisHelper.GetDay(item, responsePage);
                            month.HuangliDays.Add(day);
                        }

                        _huangliScheduler.Push(month);
                        return Task.FromResult(true);
                    }
                    else
                    {
                        Logger.Error($"链接爬取失败：{requestSite.Url}");
                    }
                }
                else
                {
                    Logger.Error($"链接爬取失败：{requestSite.Url}");
                }
            }

            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// 生成js管道。
    /// </summary>
    public class BuilderJsPipeline : CrawlerPipeline<PipelineOptions>
    {
        private readonly IScheduler _huangliScheduler;
        public BuilderJsPipeline(PipelineOptions options) : base(options)
        {
            _huangliScheduler = SchedulerManager.GetScheduler<Scheduler<HuangliMonth>>("huangliScheduler");
        }

        protected override Task<bool> ExecuteAsync(PipelineContext context)
        {
            if (_huangliScheduler.Pop() is HuangliMonth model)
            {
                var year = model.Date.Year;
                var month = model.Date.Month.ToString().PadLeft(2, '0');
                var sb = new StringBuilder();
                sb.Append($@"
window.Calendar = window.Calendar || {{}};
window.Calendar.HuangLi = window.Calendar.HuangLi || {{}};
window.Calendar.HuangLi['y{year}'] = window.Calendar.HuangLi['y{year}'] || [];");
                sb.AppendLine();
                foreach (var day in model.HuangliDays)
                {
                    sb.Append($@"
window.Calendar.HuangLi['y{year}']['d{month}{day.Day}'] = {day}
");
                }

                var savePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "jsOutput\\");
                if (!System.IO.Directory.Exists(savePath))
                {
                    System.IO.Directory.CreateDirectory(savePath);
                }

                var fileName = $"hl{year}_{month}.js";

                var fileStream = System.IO.File.Open(System.IO.Path.Combine(savePath, fileName),
                    System.IO.FileMode.OpenOrCreate);

                byte[] data = System.Text.Encoding.Default.GetBytes(sb.ToString());
                fileStream.Write(data, 0, data.Length);
                fileStream.Flush();
                fileStream.Close();

            }

            return Task.FromResult(false);
        }
    }

    public class AnalysisHelper
    {
        public static void AnalysisMonth(HuangliMonth month, Page page)
        {
            var dateNode = page.DocumentNode.SelectSingleNode("//span[@class=\"wnrl_xuanze_top_wenzi\"]");
            month.Date = DateTime.Parse(dateNode.InnerText);
        }

        public static HuangliDay GetDay(HtmlNode dayNode, Page page)
        {
            var day = new HuangliDay();
            var riqiNode = dayNode.SelectSingleNode("div[@class=\"wnrl_k_you_id_wnrl_riqi\"]");
            day.Day = int.Parse(riqiNode.InnerText);

            var yiNode = dayNode.SelectSingleNode(
                "div[@class=\"wnrl_k_you_id_wnrl_yi\"]/span[@class=\"wnrl_k_you_id_wnrl_yi_neirong\"]");

            day.Yi = yiNode?.InnerText;

            var jiNode = dayNode.SelectSingleNode(
                "div[@class=\"wnrl_k_you_id_wnrl_ji\"]/span[@class=\"wnrl_k_you_id_wnrl_ji_neirong\"]");

            day.Ji = jiNode?.InnerText;

            return day;
        }
    }

    public class ClandarPipelineOptions : PipelineOptions
    {
        public int BeginYear { get; set; }
        public int EndYear { get; set; }
    }

    // 黄历月份模型。
    public class HuangliMonth
    {
        public DateTime Date { get; set; }

        public List<HuangliDay> HuangliDays { get; set; } = new List<HuangliDay>();

        public override string ToString()
        {
            return Date.ToString(CultureInfo.InvariantCulture);
        }
    }

    public class HuangliDay
    {
        public int Day { get; set; }
        public string Yi { get; set; } // 宜
        public string Ji { get; set; } // 忌

        public override string ToString()
        {
            return string.Format("{{ 'y': '{0}', 'j': '{1}' }};", Yi, Ji);
        }
    }
}
