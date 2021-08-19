using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CalendarCrawler.Entity;
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
            var sleep = int.Parse(System.Configuration.ConfigurationManager.AppSettings["Sleep"]);


            var opt = new ClandarPipelineOptions
            {
                Name = nameof(ClandarPipeline),
                BeginYear = beginYear,
                EndYear = endYear,
                Sleep = sleep
            };

            var crawler = CrawlerBuilder.Current
                .UsePipeline<ClandarPipeline>(opt)
               // .UsePipeline<BuilderJsPipeline>(new PipelineOptions {Name = nameof(BuilderJsPipeline)}) //js输出
                .UsePipeline<BuilderDatabasePipeline>(new PipelineOptions { Name = nameof(BuilderDatabasePipeline) }) //写入数据库
                .UseMultiThread(1)
                .UseNamed("黄历爬虫")
                .Builder();

            crawler.Run();

            Console.ReadKey();
        }
    }

    /// <summary>
    /// 抓取  https://wannianrili.bmcx.com/ 中的黄历信息。
    /// </summary>
    public class ClandarPipeline : CrawlerPipeline<ClandarPipelineOptions>
    {
        private const string Api = "https://wannianrili.bmcx.com/ajax/?q={0}&v=17052214";

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
                requestSite.Referer = "https://wannianrili.bmcx.com/";
                requestSite.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8";
                requestSite.UserAgent = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/66.0.3359.181 Safari/537.36";

                // 重试3次。
                Page responsePage = null;
                var index = 4;
                do
                {
                    if (index < 4)
                    {
                        Logger.Warn($"重试：{requestSite.Url}");
                        if (responsePage != null)
                        {
                            Logger.Trace(responsePage.HtmlSource);
                        }
                        Thread.Sleep(60 * 1000);
                    }
                    responsePage = Options.Downloader.GetPage(requestSite);
                    index--;
                } while (index > 0 && (responsePage.HttpStatusCode != 200 || responsePage.DocumentNode == null));

                if (responsePage.HttpStatusCode == 200 && responsePage.DocumentNode != null)
                {
                    //获取基础信息
                    var allDayList = responsePage.DocumentNode.SelectNodes("//div[@class=\"wnrl_k_you\"]");
                    //获取详细信息
                    var allDayDetailList = responsePage.DocumentNode.SelectNodes("//div[@class=\"wnrl_k_xia_nr\"]");

                    if (allDayList != null && allDayList.Count > 0)
                    {
                        var month = new HuangliMonth();

                        AnalysisHelper.AnalysisMonth(month, responsePage);
                       
                        for (int i = 0; i < allDayList.Count; i++)
                        {
                            var day = AnalysisHelper.GetDay(allDayList[i], responsePage,i);

                            month.HuangliDays.Add(day);
                        }

                        if (allDayDetailList != null && allDayDetailList.Count > 0)
                        {
                            for (int i = 0; i < allDayDetailList.Count; i++)
                            {
                                var daydetail = AnalysisHelper.GetDayDetail(allDayDetailList[i], responsePage, i);

                                month.HuangliDaysDetails.Add(daydetail);
                            }
                        }

                        foreach (var item in month.HuangliDays)
                        {
                            var detailInfo = month.HuangliDaysDetails.Where(x => x.Id == item.Id).FirstOrDefault();
                            item.JieQi = detailInfo.JieQi;

                            month.resultList.Add(item);
                        }

                        _huangliScheduler.Push(month);
                        Logger.Info($"{month.Date:yyyy-MM} 爬取页面完成");
                        return Task.FromResult(true);
                    }
                    else
                    {
                        Logger.Fatal($"链接爬取失败：{requestSite.Url}");
                    }
                }
                else
                {
                    Logger.Fatal($"链接爬取失败：{requestSite.Url}");
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
                sb.Append($@"window.Calendar = window.Calendar || {{}};
window.Calendar.HuangLi = window.Calendar.HuangLi || {{}};
window.Calendar.HuangLi['y{year}'] = window.Calendar.HuangLi['y{year}'] || [];");
                sb.AppendLine();
                foreach (var day in model.resultList)
                {
                    sb.Append($@"window.Calendar.HuangLi['y{year}']['d{month}{day.Day.ToString().PadLeft(2, '0')} 节气：{day.JieQi} 节日：{day.JieRi}'] = {day} "+ "\r\n");
                }

                var savePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "jsOutput\\");
                if (!System.IO.Directory.Exists(savePath))
                {
                    System.IO.Directory.CreateDirectory(savePath);
                }

                var fileName = $"hl{year}_{month}.js";

                var fileStream = System.IO.File.Open(System.IO.Path.Combine(savePath, fileName),
                    System.IO.FileMode.OpenOrCreate);

                byte[] data = Encoding.UTF8.GetBytes(sb.ToString());
                fileStream.Write(data, 0, data.Length);
                fileStream.Flush();
                fileStream.Close();

                Logger.Info($"js导出成功，{fileName}");
            }

            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// 生成插入数据库管道.
    /// </summary>
    public class BuilderDatabasePipeline : CrawlerPipeline<PipelineOptions>
    {
        private static IFreeSql fsql = new FreeSql.FreeSqlBuilder()
          .UseConnectionString(FreeSql.DataType.PostgreSQL, System.Configuration.ConfigurationManager.ConnectionStrings["connString"].ToString())
          .UseAutoSyncStructure(true)
          .Build();

        private readonly IScheduler _huangliScheduler;
        public BuilderDatabasePipeline(PipelineOptions options) : base(options)
        {
            _huangliScheduler = SchedulerManager.GetScheduler<Scheduler<HuangliMonth>>("huangliScheduler");
        }
        protected override Task<bool> ExecuteAsync(PipelineContext context)
        {
            if (_huangliScheduler.Pop() is HuangliMonth model)
            {
                var year = model.Date.Year;
                var month = model.Date.Month.ToString().PadLeft(2, '0');

                foreach (var day in model.resultList)
                {
                    CalendarEntity entity = new CalendarEntity();
                    entity.LunarDate = day.Nongli;
                    entity.GregorianDate = DateTime.Parse($"{year}/{month}/{day.Day.ToString().PadLeft(2,'0')}");
                    entity.SolarTerms = day.JieQi;
                    entity.TraditionFestival = day.JieRi;
                    entity.SuitableDo = day.Yi;
                    entity.TabooDo = day.Ji;
                    entity.TraditionLunarDate = day.GanZhi;

                    fsql.InsertOrUpdate<CalendarEntity>().SetSource(entity).ExecuteAffrows();
                }

                Logger.Info($"数据库插入成功");
            }

            return Task.FromResult(false);
        }

    }

    /// <summary>
    /// 解析帮助类
    /// </summary>
    public class AnalysisHelper
    {
        public static void AnalysisMonth(HuangliMonth month, Page page)
        {
            var dateNode = page.DocumentNode.SelectSingleNode("//span[@class=\"wnrl_xuanze_top_wenzi\"]");
            month.Date = DateTime.Parse(dateNode.InnerText);
        }

        public static HuangliDay GetDay(HtmlNode dayNode, Page page,int index)
        {
            var day = new HuangliDay();
            day.Id = index;

            var riqiNode = dayNode.SelectSingleNode("div[@class=\"wnrl_k_you_id_wnrl_riqi\"]");
            day.Day = int.Parse(riqiNode.InnerText);

            var yiNode = dayNode.SelectSingleNode(
                "div[@class=\"wnrl_k_you_id_wnrl_yi\"]/span[@class=\"wnrl_k_you_id_wnrl_yi_neirong\"]");

            day.Yi = yiNode?.InnerText;

            var jiNode = dayNode.SelectSingleNode(
                "div[@class=\"wnrl_k_you_id_wnrl_ji\"]/span[@class=\"wnrl_k_you_id_wnrl_ji_neirong\"]");

            day.Ji = jiNode?.InnerText;

            var nongli = dayNode.SelectSingleNode(
                "div[@class=\"wnrl_k_you_id_wnrl_nongli\"]");
            day.Nongli = nongli?.InnerText;

            var ganzhi = dayNode.SelectSingleNode(
                "div[@class=\"wnrl_k_you_id_wnrl_nongli_ganzhi\"]");
            day.GanZhi = ganzhi?.InnerText;

            var jieri = dayNode.SelectSingleNode(
                "div[@class=\"wnrl_k_you_id_wnrl_jieri\"]/span[@class=\"wnrl_k_you_id_wnrl_jieri_neirong\"]");
            day.JieRi = jieri?.InnerText;

            return day;
        }

        public static HuangliDaysDetail GetDayDetail(HtmlNode dayNode, Page page,int index)
        {
            var day = new HuangliDaysDetail();
            day.Id = index;

            var jieqi = dayNode.SelectNodes(
                "div[@class=\"wnrl_k_xia_nr_wnrl_beizhu\"]/span[@class=\"wnrl_k_xia_nr_wnrl_beizhu_neirong\"]");
            day.JieQi = jieqi[9]?.InnerText;

            return day;
        }
    }

    /// <summary>
    /// 管道选项
    /// </summary>
    public class ClandarPipelineOptions : PipelineOptions
    {
        public int BeginYear { get; set; }
        public int EndYear { get; set; }
    }

    /// <summary>
    /// 黄历月份模型
    /// </summary>
    public class HuangliMonth
    {
        public DateTime Date { get; set; }

        public List<HuangliDay> HuangliDays { get; set; } = new List<HuangliDay>();

        public List<HuangliDaysDetail> HuangliDaysDetails { get; set; } = new List<HuangliDaysDetail>();

        public List<HuangliDay> resultList { get; set; } = new List<HuangliDay>();

        public override string ToString()
        {
            return Date.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// 黄历基础信息
    /// </summary>
    public class HuangliDay: HuangliDaysDetail
    {
        public int Id { get; set; }
        public int Day { get; set; }
        public string Yi { get; set; } // 宜
        public string Ji { get; set; } // 忌
        public string Nongli { get; set; } //农历
        public string GanZhi { get; set; } //天干地支
        public string JieRi { get; set; }  //节日

        public override string ToString()
        {
            return string.Format("{{ 'y': '{0}', 'j': '{1}' }};", Yi, Ji);
        }
    }

    /// <summary>
    /// 黄历详细信息
    /// </summary>
    public class HuangliDaysDetail
    {
        public int Id { get; set; }
        public string JieQi { get; set; } //节气


    }
}
