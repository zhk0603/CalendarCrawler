using FreeSql.DataAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CalendarCrawler.Entity
{
    /// <summary>
    /// 日历实体
    /// </summary>
    [Table(Name = "Lunar")]
    public class CalendarEntity
    {
        /// <summary>
        /// 主键
        /// </summary>
        [Column(IsIdentity = true)]
        public int Id { get; set; }
        /// <summary>
        ///     阳历
        /// </summary>
        public DateTime GregorianDate { get; set; }
        /// <summary>
        ///     农历
        /// </summary>
        public string LunarDate { get; set; }
        /// <summary>
        ///     天干地支
        /// </summary>
        public string TraditionLunarDate { get; set; }
        /// <summary>
        /// 传统节日
        /// </summary>
        public string TraditionFestival { get; set; }
        /// <summary>
        /// 节气
        /// </summary>
        public string SolarTerms { get; set; }
        /// <summary>
        /// 宜
        /// </summary>
        public string SuitableDo { get; set; }
        /// <summary>
        /// 忌
        /// </summary>
        public string TabooDo { get; set; }
    }
}
