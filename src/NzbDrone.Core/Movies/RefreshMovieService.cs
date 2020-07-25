using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Movies.AlternativeTitles;
using NzbDrone.Core.Movies.Commands;
using NzbDrone.Core.Movies.Credits;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Movies.Translations;

namespace NzbDrone.Core.Movies
{
    public class JustWatchPayload
    {
        public string age_certifications { get; set; }
        public string content_types { get; set; }
        public string presentation_types { get; set; }
        public string providers { get; set; }
        public string genres { get; set; }
        public string languages { get; set; }
        public int release_year_from { get; set; }
        public int release_year_until { get; set; }
        public string monetization_types { get; set; }
        public string min_price { get; set; }
        public string max_price { get; set; }
        public string nationwide_cinema_releases_only { get; set; }
        public string scoring_filter_types { get; set; }
        public string cinema_release { get; set; }
        public string query { get; set; }
        public string page { get; set; }
        public string page_size { get; set; }
        public string timeline_type { get; set; }
    }

    public class FullPaths
    {
        public string MOVIE_DETAIL_OVERVIEW { get; set; }
    }

    public class Urls
    {
        public string standard_web { get; set; }
    }

    public class Offer
    {
        public string monetization_type { get; set; }
        public int provider_id { get; set; }
        public double retail_price { get; set; }
        public string currency { get; set; }
        public Urls urls { get; set; }
        public List<object> subtitle_languages { get; set; }
        public List<object> audio_languages { get; set; }
        public string presentation_type { get; set; }
        public string date_created_provider_id { get; set; }
        public string date_created { get; set; }
        public string country { get; set; }
        public double? last_change_retail_price { get; set; }
        public double? last_change_difference { get; set; }
        public double? last_change_percent { get; set; }
        public string last_change_date { get; set; }
        public string last_change_date_provider_id { get; set; }
    }

    public class Scoring
    {
        public string provider_type { get; set; }
        public double value { get; set; }
    }

    public class Item
    {
        public int id { get; set; }
        public string title { get; set; }
        public string full_path { get; set; }
        public FullPaths full_paths { get; set; }
        public string poster { get; set; }
        public string short_description { get; set; }
        public int original_release_year { get; set; }
        public double tmdb_popularity { get; set; }
        public string object_type { get; set; }
        public string original_title { get; set; }
        public List<Offer> offers { get; set; }
        public List<Scoring> scoring { get; set; }
        public string original_language { get; set; }
        public int runtime { get; set; }
        public string age_certification { get; set; }
    }

    public class RootObject
    {
        public int page { get; set; }
        public int page_size { get; set; }
        public int total_pages { get; set; }
        public int total_results { get; set; }
        public List<Item> items { get; set; }
    }

    public class RefreshMovieService : IExecute<RefreshMovieCommand>
    {
        private readonly IProvideMovieInfo _movieInfo;
        private readonly IMovieService _movieService;
        private readonly IMovieTranslationService _movieTranslationService;
        private readonly IAlternativeTitleService _titleService;
        private readonly ICreditService _creditService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDiskScanService _diskScanService;
        private readonly ICheckIfMovieShouldBeRefreshed _checkIfMovieShouldBeRefreshed;
        private readonly IConfigService _configService;

        private readonly Logger _logger;

        public RefreshMovieService(IProvideMovieInfo movieInfo,
                                    IMovieService movieService,
                                    IMovieTranslationService movieTranslationService,
                                    IAlternativeTitleService titleService,
                                    ICreditService creditService,
                                    IEventAggregator eventAggregator,
                                    IDiskScanService diskScanService,
                                    ICheckIfMovieShouldBeRefreshed checkIfMovieShouldBeRefreshed,
                                    IConfigService configService,
                                    Logger logger)
        {
            _movieInfo = movieInfo;
            _movieService = movieService;
            _movieTranslationService = movieTranslationService;
            _titleService = titleService;
            _creditService = creditService;
            _eventAggregator = eventAggregator;
            _diskScanService = diskScanService;
            _checkIfMovieShouldBeRefreshed = checkIfMovieShouldBeRefreshed;
            _configService = configService;
            _logger = logger;
        }

        public class OmdbObject
        {
            public string Title { get; set; }
            public string Year { get; set; }
            public string Rated { get; set; }
            public string Released { get; set; }
            public string Runtime { get; set; }
            public string Genre { get; set; }
            public string Director { get; set; }
            public string Writer { get; set; }
            public string Actors { get; set; }
            public string Plot { get; set; }
            public string Language { get; set; }
            public string Country { get; set; }
            public string Awards { get; set; }
            public string Poster { get; set; }
            public string Metascore { get; set; }
            public string imdbRating { get; set; }
            public string imdbVotes { get; set; }
            public string imdbID { get; set; }
            public string Type { get; set; }
            public string tomatoMeter { get; set; }
            public string tomatoImage { get; set; }
            public string tomatoRating { get; set; }
            public string tomatoReviews { get; set; }
            public string tomatoFresh { get; set; }
            public string tomatoRotten { get; set; }
            public string tomatoConsensus { get; set; }
            public string tomatoUserMeter { get; set; }
            public string tomatoUserRating { get; set; }
            public string tomatoUserReviews { get; set; }
            public string tomatoURL { get; set; }
            public string DVD { get; set; }
            public string BoxOffice { get; set; }
            public string Production { get; set; }
            public string Website { get; set; }
            public string Response { get; set; }
        }

        public Tuple<DateTime?, DateTime?> determineReleaseDates(DateTime? tmdbInCinemas, DateTime? tmdbPhysicalRelease, string imdbId)
        {
            //Summary:
            // if THEMOVIEDB returns both a physical and cinemas date use those and stop
            // otherwise THEMOVIEDB returned partial or no information so check OMDBAPI
            // if OMDBAPI returns both a physical and cinema date use those and stop
            // at this pointif we are still going, both OMDBAPI and THEMOVIEDB each returned no or partial information
            // if both OMDBAPI and THEMOVIEDB returned partial information and the partial info doesnt overlap construct full information
            // if the full information constructed makes sense, use that and stop
            // at this point we know full information is not going to be available
            // if THEMOVIEDB had partial information, use it and stop
            // if OMDBAPI returns partial information use it and stop
            // if we got here both OMDBAPI and THEMOVIEDB return no information, no information is propagated and we stop.
            if (tmdbInCinemas.HasValue && tmdbPhysicalRelease.HasValue)
            {
                if (tmdbPhysicalRelease.Value.Subtract(tmdbInCinemas.Value).Duration().TotalSeconds < 60 * 60 * 24 * 15)
                {
                    tmdbPhysicalRelease = tmdbInCinemas;
                }

                if (tmdbInCinemas <= tmdbPhysicalRelease)
                {
                    return Tuple.Create(tmdbInCinemas, tmdbPhysicalRelease);
                }
            }

            //lets augment the releasedate information with info from omdbapi
            string data;
            using (WebClient client = new WebClient())
            {
                data = client.DownloadString("http://www.omdbapi.com/?i=" + imdbId + "&tomatoes=true&plot=short&r=json&apikey=" + _configService.OmdbApiKey);
            }

            OmdbObject j1 = Newtonsoft.Json.JsonConvert.DeserializeObject<OmdbObject>(data);
            DateTime? omdbInCinemas;
            DateTime? omdbPhysicalRelease;
            if (j1.Released != "N/A")
            {
                omdbInCinemas = DateTime.Parse(j1.Released);
            }
            else
            {
                omdbInCinemas = null;
            }

            if (j1.DVD != "N/A")
            {
                omdbPhysicalRelease = DateTime.Parse(j1.DVD);
            }
            else
            {
                omdbPhysicalRelease = null;
            }

            if (omdbInCinemas.HasValue && omdbPhysicalRelease.HasValue)
            {
                if (omdbPhysicalRelease.Value.Subtract(omdbInCinemas.Value).Duration().TotalSeconds < 60 * 60 * 24 * 15)
                {
                    omdbPhysicalRelease = omdbInCinemas;
                }

                if (omdbInCinemas <= omdbPhysicalRelease)
                {
                    return Tuple.Create(omdbInCinemas, omdbPhysicalRelease);
                }
            }

            //now we know that we either have partial information or no information
            if (omdbInCinemas.HasValue && tmdbPhysicalRelease.HasValue)
            {
                if (omdbInCinemas <= tmdbPhysicalRelease)
                {
                    return Tuple.Create(omdbInCinemas, tmdbPhysicalRelease);
                }
            }

            if (omdbPhysicalRelease.HasValue && tmdbInCinemas.HasValue)
            {
                if (tmdbInCinemas <= omdbPhysicalRelease)
                {
                    return Tuple.Create(tmdbInCinemas, omdbPhysicalRelease);
                }
            }

            if ((omdbInCinemas.HasValue && !omdbPhysicalRelease.HasValue) || (!omdbInCinemas.HasValue && omdbPhysicalRelease.HasValue))
            {
                return Tuple.Create(omdbInCinemas, omdbPhysicalRelease);
            }
            else if ((tmdbInCinemas.HasValue && !tmdbPhysicalRelease.HasValue) || (!tmdbInCinemas.HasValue && tmdbPhysicalRelease.HasValue))
            {
                return Tuple.Create(tmdbInCinemas, tmdbPhysicalRelease);
            }

            if (omdbPhysicalRelease.HasValue)
            {
                omdbInCinemas = null;
                return Tuple.Create(omdbInCinemas, omdbPhysicalRelease);
            }
            else if (tmdbPhysicalRelease.HasValue)
            {
                tmdbInCinemas = null;
                return Tuple.Create(tmdbInCinemas, tmdbPhysicalRelease);
            }
            else if (omdbInCinemas.HasValue)
            {
                omdbPhysicalRelease = null;
                return Tuple.Create(omdbInCinemas, omdbPhysicalRelease);
            }
            else if (tmdbInCinemas.HasValue)
            {
                tmdbPhysicalRelease = null;
                return Tuple.Create(tmdbInCinemas, tmdbPhysicalRelease);
            }

            omdbInCinemas = null;
            omdbPhysicalRelease = null;
            return Tuple.Create(omdbInCinemas, omdbPhysicalRelease);
        }

        private void RefreshMovieInfo(Movie movie)
        {
            _logger.ProgressInfo("Updating Info for {0}", movie.Title);

            Movie movieInfo;
            List<Credit> credits;

            try
            {
                var tuple = _movieInfo.GetMovieInfo(movie.TmdbId);
                movieInfo = tuple.Item1;
                credits = tuple.Item2;
            }
            catch (MovieNotFoundException)
            {
                if (movie.Status != MovieStatusType.Deleted)
                {
                    movie.Status = MovieStatusType.Deleted;
                    _movieService.UpdateMovie(movie);
                    _logger.Debug("Movie marked as deleted on tmdb for {0}", movie.Title);
                    _eventAggregator.PublishEvent(new MovieUpdatedEvent(movie));
                }

                throw;
            }

            if (movie.TmdbId != movieInfo.TmdbId)
            {
                _logger.Warn("Movie '{0}' (Tmdbid {1}) was replaced with '{2}' (Tmdbid {3}), because the original was a duplicate.", movie.Title, movie.TmdbId, movieInfo.Title, movieInfo.TmdbId);
                movie.TmdbId = movieInfo.TmdbId;
            }

            if (movieInfo.Year != 0 && _configService.OmdbApiKey.IsNotNullOrWhiteSpace())
            {
                Tuple<DateTime?, DateTime?> t = determineReleaseDates(movieInfo.InCinemas, movieInfo.PhysicalRelease, movieInfo.ImdbId);
                movieInfo.InCinemas = t.Item1;
                movieInfo.PhysicalRelease = t.Item2;
            }

            var now = DateTime.Now;

            movieInfo.Status = MovieStatusType.Announced;

            if (movieInfo.InCinemas.HasValue && now > movieInfo.InCinemas)
            {
                movieInfo.Status = MovieStatusType.InCinemas;

                if (!movieInfo.PhysicalRelease.HasValue && !movieInfo.DigitalRelease.HasValue && now > movieInfo.InCinemas.Value.AddDays(90))
                {
                    movieInfo.Status = MovieStatusType.Released;
                }
            }

            if (movieInfo.PhysicalRelease.HasValue && now >= movieInfo.PhysicalRelease)
            {
                movieInfo.Status = MovieStatusType.Released;
            }

            if (movieInfo.DigitalRelease.HasValue && now >= movieInfo.DigitalRelease)
            {
                movieInfo.Status = MovieStatusType.Released;
            }

            movie.Title = movieInfo.Title;
            movie.TitleSlug = movieInfo.TitleSlug;
            movie.ImdbId = movieInfo.ImdbId;
            movie.Overview = movieInfo.Overview;
            movie.Status = movieInfo.Status;
            movie.CleanTitle = movieInfo.CleanTitle;
            movie.SortTitle = movieInfo.SortTitle;
            movie.LastInfoSync = DateTime.UtcNow;
            movie.Runtime = movieInfo.Runtime;
            movie.Images = movieInfo.Images;
            movie.Ratings = movieInfo.Ratings;
            movie.Collection = movieInfo.Collection;
            movie.Genres = movieInfo.Genres;
            movie.Certification = movieInfo.Certification;
            movie.InCinemas = movieInfo.InCinemas;
            movie.Website = movieInfo.Website;
            movie.Year = movieInfo.Year;
            movie.SecondaryYear = movieInfo.SecondaryYear;
            movie.PhysicalRelease = movieInfo.PhysicalRelease;
            movie.DigitalRelease = movieInfo.DigitalRelease;
            movie.YouTubeTrailerId = movieInfo.YouTubeTrailerId;
            movie.Studio = movieInfo.Studio;
            movie.OriginalTitle = movieInfo.OriginalTitle;
            movie.OriginalLanguage = movieInfo.OriginalLanguage;
            movie.HasPreDBEntry = movieInfo.HasPreDBEntry;
            movie.Recommendations = movieInfo.Recommendations;

            //put Justwatch Code in here:
            string enableNetflix = _configService.EnableNetflix;
            string enablePrimeVideo = _configService.EnablePrimeVideo;
            string enableHoopla = _configService.EnableHoopla;
            string enableTubiTV = _configService.EnableTubiTV;

            string locale = _configService.JustwatchLocale;

            if (enableNetflix == "enabled")
            {
                movieInfo.NetflixUrl = null;
            }

            if (enablePrimeVideo == "enabled")
            {
                movieInfo.PrimeVideoUrl = null;
            }

            if (enableHoopla == "enabled")
            {
                movieInfo.HooplaUrl = null;
            }

            if (enableTubiTV == "enabled")
            {
                movieInfo.TubiTVUrl = null;
            }

            movieInfo.JustwatchUrl = null;
            if (movieInfo.Status == MovieStatusType.Released)
            {
                string api_url = "https://apis.justwatch.com/content/titles/" + locale + "/popular";
                var title = movieInfo.Title;
                for (int alternativeTitlesIndex = -1; alternativeTitlesIndex < movieInfo.AlternativeTitles.Count; alternativeTitlesIndex++)
                {
                    if (alternativeTitlesIndex >= 0)
                    {
                        title = movieInfo.AlternativeTitles[alternativeTitlesIndex].Title;
                    }

                    var payload = new JustWatchPayload();
                    payload.age_certifications = null;
                    payload.content_types = null;
                    payload.presentation_types = null;
                    payload.providers = null;
                    payload.genres = null;
                    payload.languages = null;
                    payload.release_year_from = movieInfo.Year - 1;
                    payload.release_year_until = movieInfo.Year + 1;
                    payload.monetization_types = null;
                    payload.min_price = null;
                    payload.max_price = null;
                    payload.nationwide_cinema_releases_only = null;
                    payload.scoring_filter_types = null;
                    payload.cinema_release = null;
                    payload.query = title.ToString();
                    payload.page = null;
                    payload.page_size = null;
                    payload.timeline_type = null;

                    string json = JsonConvert.SerializeObject(payload);
                    byte[] byteArray = Encoding.UTF8.GetBytes(json);

                    HttpWebRequest rquest = (HttpWebRequest)WebRequest.Create(api_url);
                    rquest.Method = "POST";
                    using (var st = rquest.GetRequestStream())
                    {
                        st.Write(byteArray, 0, byteArray.Length);
                    }

                    var rsponse = (HttpWebResponse)rquest.GetResponse();
                    var rsponseString = new StreamReader(rsponse.GetResponseStream()).ReadToEnd();
                    rsponse.Close();

                    RootObject rsponseObject = JsonConvert.DeserializeObject<RootObject>(rsponseString);

                    for (int i = 0; (rsponseObject != null) && (rsponseObject.items != null) && (i < rsponseObject.items.Count); i++)
                    {
                        for (int j = 0; (rsponseObject.items[i].scoring != null) && (j < rsponseObject.items[i].scoring.Count); j++)
                        {
                            if (rsponseObject.items[i].scoring[j].provider_type == "tmdb:id")
                            {
                                if (rsponseObject.items[i].scoring[j].value == movieInfo.TmdbId)
                                {
                                    movieInfo.JustwatchUrl = "https://www.justwatch.com" + rsponseObject.items[i].full_path;
                                    if (enableNetflix == "enabled" || enablePrimeVideo == "enabled" || enableHoopla == "enabled" || enableTubiTV == "enabled")
                                    {
                                        for (int k = 0; (rsponseObject.items[i].offers != null) && (k < rsponseObject.items[i].offers.Count); k++)
                                        {
                                            if (enableNetflix == "enabled" && rsponseObject.items[i].offers[k].urls.standard_web.Contains("http://www.netflix.com/title/"))
                                            {
                                                movieInfo.NetflixUrl = rsponseObject.items[i].offers[k].urls.standard_web;
                                            }

                                            if (enablePrimeVideo == "enabled" && rsponseObject.items[i].offers[k].urls.standard_web.Contains("https://www.primevideo.com/detail/"))
                                            {
                                                movieInfo.PrimeVideoUrl = rsponseObject.items[i].offers[k].urls.standard_web;
                                            }

                                            if (enableHoopla == "enabled" && rsponseObject.items[i].offers[k].urls.standard_web.Contains("https://www.hoopladigital.com/title/"))
                                            {
                                                movieInfo.HooplaUrl = rsponseObject.items[i].offers[k].urls.standard_web;
                                            }

                                            if (enableTubiTV == "enabled" && rsponseObject.items[i].offers[k].urls.standard_web.Contains("https://tubitv.com/movies/"))
                                            {
                                                movieInfo.TubiTVUrl = rsponseObject.items[i].offers[k].urls.standard_web;
                                            }
                                        }
                                    }
                                }

                                break;
                            }
                        }
                    }
                }
            }

            if (_configService.EnableNetflix == "disabledKeep")
            {
                movieInfo.NetflixUrl = movie.NetflixUrl;
            }

            if (_configService.EnablePrimeVideo == "disabledKeep")
            {
                movieInfo.PrimeVideoUrl = movie.PrimeVideoUrl;
            }

            if (_configService.EnableHoopla == "disabledKeep")
            {
                movieInfo.HooplaUrl = movie.HooplaUrl;
            }

            if (_configService.EnableTubiTV == "disabledKeep")
            {
                movieInfo.TubiTVUrl = movie.TubiTVUrl;
            }

            bool leftNetflix = movie.NetflixUrl != null && movieInfo.NetflixUrl == null;
            bool leftPrimeVideo = movie.PrimeVideoUrl != null && movieInfo.PrimeVideoUrl == null;
            bool leftTubiTV = movie.TubiTVUrl != null && movieInfo.TubiTVUrl == null;
            bool leftHoopla = movie.HooplaUrl != null && movieInfo.HooplaUrl == null;

            if (!movie.Monitored && (leftNetflix || leftPrimeVideo || leftTubiTV || leftHoopla))
            {
                movie.Monitored = true;
            }

            bool onNetflix = movieInfo.NetflixUrl != null;
            bool onPrimeVideo = movieInfo.PrimeVideoUrl != null;
            bool onTubiTV = movieInfo.TubiTVUrl != null;
            bool onHoopla = movieInfo.HooplaUrl != null;

            if (movie.Monitored)
            {
                if (((_configService.IgnoreNetflixTitles == true) && onNetflix) ||
                    ((_configService.IgnorePrimeVideoTitles == true) && onPrimeVideo) ||
                    ((_configService.IgnoreTubiTVTitles == true) && onTubiTV) ||
                    ((_configService.IgnoreHooplaTitles == true) && onHoopla))
                {
                    movie.Monitored = false;
                }
            }

            movie.JustwatchUrl = movieInfo.JustwatchUrl;
            movie.NetflixUrl = movieInfo.NetflixUrl;
            movie.PrimeVideoUrl = movieInfo.PrimeVideoUrl;
            movie.HooplaUrl = movieInfo.HooplaUrl;
            movie.TubiTVUrl = movieInfo.TubiTVUrl;

            try
            {
                movie.Path = new DirectoryInfo(movie.Path).FullName;
                movie.Path = movie.Path.GetActualCasing();
            }
            catch (Exception e)
            {
                _logger.Warn(e, "Couldn't update movie path for " + movie.Path);
            }

            movie.AlternativeTitles = _titleService.UpdateTitles(movieInfo.AlternativeTitles, movie);
            _movieTranslationService.UpdateTranslations(movieInfo.Translations, movie);

            _movieService.UpdateMovie(new List<Movie> { movie }, true);
            _creditService.UpdateCredits(credits, movie);

            _logger.Debug("Finished movie refresh for {0}", movie.Title);
            _eventAggregator.PublishEvent(new MovieUpdatedEvent(movie));
        }

        private void RescanMovie(Movie movie, bool isNew, CommandTrigger trigger)
        {
            var rescanAfterRefresh = _configService.RescanAfterRefresh;
            var shouldRescan = true;

            if (isNew)
            {
                _logger.Trace("Forcing refresh of {0}. Reason: New movie", movie);
                shouldRescan = true;
            }
            else if (rescanAfterRefresh == RescanAfterRefreshType.Never)
            {
                _logger.Trace("Skipping refresh of {0}. Reason: never rescan after refresh", movie);
                shouldRescan = false;
            }
            else if (rescanAfterRefresh == RescanAfterRefreshType.AfterManual && trigger != CommandTrigger.Manual)
            {
                _logger.Trace("Skipping refresh of {0}. Reason: not after automatic scans", movie);
                shouldRescan = false;
            }

            if (!shouldRescan)
            {
                return;
            }

            try
            {
                _diskScanService.Scan(movie);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Couldn't rescan movie {0}", movie);
            }
        }

        public void Execute(RefreshMovieCommand message)
        {
            var trigger = message.Trigger;
            var isNew = message.IsNewMovie;
            _eventAggregator.PublishEvent(new MovieRefreshStartingEvent(message.Trigger == CommandTrigger.Manual));

            if (message.MovieIds.Any())
            {
                foreach (var movieId in message.MovieIds)
                {
                    var movie = _movieService.GetMovie(movieId);

                    try
                    {
                        RefreshMovieInfo(movie);
                        RescanMovie(movie, isNew, trigger);
                    }
                    catch (MovieNotFoundException)
                    {
                        _logger.Error("Movie '{0}' (imdbid {1}) was not found, it may have been removed from The Movie Database.", movie.Title, movie.ImdbId);
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, "Couldn't refresh info for {0}", movie);
                        RescanMovie(movie, isNew, trigger);
                        throw;
                    }
                }
            }
            else
            {
                var allMovie = _movieService.GetAllMovies().OrderBy(c => c.SortTitle).ToList();

                var updatedTMDBMovies = new HashSet<int>();

                if (message.LastStartTime.HasValue && message.LastStartTime.Value.AddDays(14) > DateTime.UtcNow)
                {
                    updatedTMDBMovies = _movieInfo.GetChangedMovies(message.LastStartTime.Value);
                }

                foreach (var movie in allMovie)
                {
                    if ((updatedTMDBMovies.Count == 0 && _checkIfMovieShouldBeRefreshed.ShouldRefresh(movie)) || updatedTMDBMovies.Contains(movie.TmdbId) || message.Trigger == CommandTrigger.Manual)
                    {
                        try
                        {
                            RefreshMovieInfo(movie);
                        }
                        catch (MovieNotFoundException)
                        {
                            _logger.Error("Movie '{0}' (imdbid {1}) was not found, it may have been removed from The Movie Database.", movie.Title, movie.ImdbId);
                            continue;
                        }
                        catch (Exception e)
                        {
                            _logger.Error(e, "Couldn't refresh info for {0}", movie);
                        }

                        RescanMovie(movie, false, trigger);
                    }
                    else
                    {
                        _logger.Info("Skipping refresh of movie: {0}", movie.Title);
                        RescanMovie(movie, false, trigger);
                    }
                }
            }
        }
    }
}
