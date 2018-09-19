using NzbDrone.Api.REST;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Api.Config
{
    public class UiConfigResource : RestResource
    {
        //Calendar
        public int FirstDayOfWeek { get; set; }
        public string CalendarWeekColumnHeader { get; set; }

        //Dates
        public string ShortDateFormat { get; set; }
        public string LongDateFormat { get; set; }
        public string TimeFormat { get; set; }
        public bool ShowRelativeDates { get; set; }
        
        //JustWatch
        public string JustWatchLocale { get; set; }
        public string EnableNetflix { get; set; }
        public string IgnoreNetflixTitles { get; set; }
        public string EnablePrimeVideo { get; set; }
        public string IgnorePrimeVideoTitles { get; set; }
        public string EnableHoopla { get; set; }
        public string IgnoreHooplaTitles { get; set; }
        public string EnableTubi { get; set; }
        public string IgnoreTubiTitles { get; set; }
        public string MonitorLeaveNetflixPrimeVideo { get; set; }

        public bool EnableColorImpairedMode { get; set; }
    }

    public static class UiConfigResourceMapper
    {
        public static UiConfigResource ToResource(IConfigService model)
        {
            return new UiConfigResource
            {
                FirstDayOfWeek = model.FirstDayOfWeek,
                CalendarWeekColumnHeader = model.CalendarWeekColumnHeader,
                
                ShortDateFormat = model.ShortDateFormat,
                LongDateFormat = model.LongDateFormat,
                TimeFormat = model.TimeFormat,
                ShowRelativeDates = model.ShowRelativeDates,
                JustWatchLocale = model.JustWatchLocale,
                EnableNetflix = model.EnableNetflix,
                IgnoreNetflixTitles = model.IgnoreNetflixTitles,
                EnablePrimeVideo = model.EnablePrimeVideo,
                IgnorePrimeVideoTitles = model.IgnorePrimeVideoTitles,
                EnableHoopla = model.EnableHoopla,
                IgnoreHooplaTitles = model.IgnoreHooplaTitles,
                EnableTubi = model.EnableTubi,
                IgnoreTubiTitles = model.IgnoreTubiTitles,
                MonitorLeaveNetflixPrimeVideo = model.MonitorLeaveNetflixPrimeVideo,

                EnableColorImpairedMode = model.EnableColorImpairedMode,
            };
        }
    }
}
