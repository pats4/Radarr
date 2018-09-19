using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
 using System.ServiceModel;
 using NLog;
using System.IO;
using NzbDrone.Core.Configuration;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using System.Text;
using NzbDrone.Common.Cloud;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.SkyHook.Resource;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.PreDB;
using NzbDrone.Core.Movies;
using System.Threading;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Profiles;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.NetImport.ImportExclusions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.RadarrAPI;
 using NzbDrone.Core.Movies.AlternativeTitles;

namespace NzbDrone.Core.MetadataSource.SkyHook
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

    public class SkyHookProxy : IProvideMovieInfo, ISearchForNewMovie, IDiscoverNewMovies
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        
        private readonly IHttpRequestBuilderFactory _movieBuilder;
        private readonly ITmdbConfigService _configService;
        private readonly IMovieService _movieService;
        private readonly IConfigService _settingsService;
        private readonly IPreDBService _predbService;
        private readonly IImportExclusionsService _exclusionService;
        private readonly IAlternativeTitleService _altTitleService;
        private readonly IRadarrAPIClient _radarrAPI;


        public SkyHookProxy(IHttpClient httpClient, IRadarrCloudRequestBuilder requestBuilder, ITmdbConfigService configService, IMovieService movieService,
                            IPreDBService predbService, IImportExclusionsService exclusionService, IAlternativeTitleService altTitleService, IRadarrAPIClient radarrAPI, IConfigService settingsService, Logger logger)
        {
            _httpClient = httpClient;
            _movieBuilder = requestBuilder.TMDB;
            _configService = configService;
            _movieService = movieService;
            _predbService = predbService;
            _settingsService = settingsService;
            _exclusionService = exclusionService;
            _altTitleService = altTitleService;
            _radarrAPI = radarrAPI;

            _logger = logger;
        }

        public Movie GetMovieInfo(int TmdbId, Profile profile = null, bool hasPreDBEntry = false)
        {
            var langCode = profile != null ? IsoLanguages.Get(profile.Language)?.TwoLetterCode ?? "en" : "en";

            var request = _movieBuilder.Create()
               .SetSegment("route", "movie")
               .SetSegment("id", TmdbId.ToString())
               .SetSegment("secondaryRoute", "")
               .AddQueryParam("append_to_response", "alternative_titles,release_dates,videos")
               .AddQueryParam("language", langCode.ToUpper())
               // .AddQueryParam("country", "US")
               .Build();

            request.AllowAutoRedirect = true;
            request.SuppressHttpError = true;

            var response = _httpClient.Get<MovieResourceRoot>(request);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new MovieNotFoundException("Movie not found.");
            }
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new HttpException(request, response);
            }

            if (response.Headers.ContentType != HttpAccept.JsonCharset.Value)
            {
                throw new HttpException(request, response);
            }

            // The dude abides, so should us, Lets be nice to TMDb
            // var allowed = int.Parse(response.Headers.GetValues("X-RateLimit-Limit").First()); // get allowed
            // var reset = long.Parse(response.Headers.GetValues("X-RateLimit-Reset").First()); // get time when it resets
            if (response.Headers.ContainsKey("X-RateLimit-Remaining"))
            {
                var remaining = int.Parse(response.Headers.GetValues("X-RateLimit-Remaining").First());
                if (remaining <= 5)
                {
                    _logger.Trace("Waiting 5 seconds to get information for the next 35 movies");
                    Thread.Sleep(5000);
                }
            }

            var resource = response.Resource;
            if (resource.status_message != null)
            {
                if (resource.status_code == 34)
                {
                    _logger.Warn("Movie with TmdbId {0} could not be found. This is probably the case when the movie was deleted from TMDB.", TmdbId);
                    return null;
                }

                _logger.Warn(resource.status_message);
                return null;
            }

            var movie = new Movie();
            var altTitles = new List<AlternativeTitle>();

            if (langCode != "en")
            {
                var iso = IsoLanguages.Find(resource.original_language);
                if (iso != null)
                {
                    altTitles.Add(new AlternativeTitle(resource.original_title, SourceType.TMDB, TmdbId, iso.Language));
                }
            }

            foreach (var alternativeTitle in resource.alternative_titles.titles)
            {
                if (alternativeTitle.iso_3166_1.ToLower() == langCode)
                {
                    altTitles.Add(new AlternativeTitle(alternativeTitle.title, SourceType.TMDB, TmdbId, IsoLanguages.Find(alternativeTitle.iso_3166_1.ToLower())?.Language ?? Language.English));
                }
                else if (alternativeTitle.iso_3166_1.ToLower() == "us")
                {
                    altTitles.Add(new AlternativeTitle(alternativeTitle.title, SourceType.TMDB, TmdbId, Language.English));
                }
            }

            movie.TmdbId = TmdbId;
            movie.ImdbId = resource.imdb_id;
            movie.Title = resource.title;
            movie.TitleSlug = Parser.Parser.ToUrlSlug(resource.title);
            movie.CleanTitle = resource.title.CleanSeriesTitle();
            movie.SortTitle = Parser.Parser.NormalizeTitle(resource.title);
            movie.Overview = resource.overview;
            movie.Website = resource.homepage;

            if (resource.release_date.IsNotNullOrWhiteSpace())
            {
                movie.InCinemas = DateTime.Parse(resource.release_date);

                // get the lowest year in all release date
                var lowestYear = new List<int>();
                foreach (ReleaseDates releaseDates in resource.release_dates.results)
                {
                    foreach (ReleaseDate releaseDate in releaseDates.release_dates)
                    {
                        lowestYear.Add(DateTime.Parse(releaseDate.release_date).Year);
                    }
                }
                movie.Year = lowestYear.Min();
            }

            movie.TitleSlug += "-" + movie.TmdbId.ToString();

            movie.Images.Add(_configService.GetCoverForURL(resource.poster_path, MediaCoverTypes.Poster));//TODO: Update to load image specs from tmdb page!
            movie.Images.Add(_configService.GetCoverForURL(resource.backdrop_path, MediaCoverTypes.Fanart));
            movie.Runtime = resource.runtime;

            foreach (ReleaseDates releaseDates in resource.release_dates.results)
            {
                foreach (ReleaseDate releaseDate in releaseDates.release_dates)
                {
                    if (releaseDate.type == 5 || releaseDate.type == 4)
                    {
                        if (movie.PhysicalRelease.HasValue)
                        {
                            if (movie.PhysicalRelease.Value.After(DateTime.Parse(releaseDate.release_date)))
                            {
                                movie.PhysicalRelease = DateTime.Parse(releaseDate.release_date); //Use oldest release date available.
                                movie.PhysicalReleaseNote = releaseDate.note;
                            }
                        }
                        else
                        {
                            movie.PhysicalRelease = DateTime.Parse(releaseDate.release_date);
                            movie.PhysicalReleaseNote = releaseDate.note;
                        }
                    }
                }
            }

            movie.Ratings = new Ratings();
            movie.Ratings.Votes = resource.vote_count;
            movie.Ratings.Value = (decimal)resource.vote_average;

            foreach (Genre genre in resource.genres)
            {
                movie.Genres.Add(genre.name);
            }

            var now = DateTime.Now;
            //handle the case when we have both theatrical and physical release dates
            if (movie.InCinemas.HasValue && movie.PhysicalRelease.HasValue)
            {
                if (now < movie.InCinemas)
                    movie.Status = MovieStatusType.Announced;
                else if (now >= movie.InCinemas)
                    movie.Status = MovieStatusType.InCinemas;
                if (now >= movie.PhysicalRelease)
                    movie.Status = MovieStatusType.Released;
            }
            //handle the case when we have theatrical release dates but we dont know the physical release date
            else if (movie.InCinemas.HasValue && (now >= movie.InCinemas))
            {
                movie.Status = MovieStatusType.InCinemas;
            }
            //handle the case where we only have a physical release date
            else if (movie.PhysicalRelease.HasValue && (now >= movie.PhysicalRelease))
            {
                movie.Status = MovieStatusType.Released;
            }
            //otherwise the title has only been announced
            else
            {
                movie.Status = MovieStatusType.Announced;
            }
            //since TMDB lacks alot of information lets assume that stuff is released if its been in cinemas for longer than 3 months.
            if (!movie.PhysicalRelease.HasValue && (movie.Status == MovieStatusType.InCinemas) && (((DateTime.Now).Subtract(movie.InCinemas.Value)).TotalSeconds > 60 * 60 * 24 * 30 * 3))
            {
                movie.Status = MovieStatusType.Released;
            }

            if (!hasPreDBEntry)
            {
                if (_predbService.HasReleases(movie))
                {
                    movie.HasPreDBEntry = true;
                }
                else
                {
                    movie.HasPreDBEntry = false;
                }
            }

            if (resource.videos != null)
            {
                foreach (Video video in resource.videos.results)
                {
                    if (video.type == "Trailer" && video.site == "YouTube")
                    {
                        if (video.key != null)
                        {
                            movie.YouTubeTrailerId = video.key;
                            break;
                        }
                    }
                }
            }

            if (resource.production_companies != null)
            {
                if (resource.production_companies.Any())
                {
                    movie.Studio = resource.production_companies[0].name;
                }
            }

            //need to add the actual title in so it gets searched in allflicks too
            altTitles.Add(new AlternativeTitle(resource.title, SourceType.TMDB, TmdbId, Language.English));
            movie.AlternativeTitles.AddRange(altTitles);

            string enableNetflix = _settingsService.EnableNetflix;
            string enablePrimeVideo = _settingsService.EnablePrimeVideo;
            string enableHoopla = _settingsService.EnableHoopla;
            string enableTubi = _settingsService.EnableTubi;
            /*if (enableNetflix == "disabledKeep")
            {
                enableNetflix = "disabled";
            }
            if (enablePrimeVideo == "disabledKeep")
            {
                enablePrimeVideo = "disabled";
            }
            */
            string locale = _settingsService.JustWatchLocale;
            /*string locale;
            switch (countryCode)
            {
                case "ca":
                    {
                        locale = "en_CA";
                        break;
                    }
                case "us":
                default:
                    {
                        locale = "en_US";
                        break;
                    }
            }*/
            /*old code --leaving here for reference
            foreach (var title in movie.AlternativeTitles)
            {
                string tempTitle = referer + "movie/" + ToUrlSlug(title.ToString(), true);
                for (int i = -1; i < 2; i++)
                {
                    int tempYear = movie.Year + i;
                    movie.NetflixUrl = tempTitle + "-" + tempYear.ToString();
                    string r1;
                    try
                    {
                        using (WebClient client = new WebClient())
                        {
                            r1 = client.DownloadString(movie.NetflixUrl);
                        }
                    }
                    catch (WebException ex)
                    {
                        if (ex.Status == WebExceptionStatus.ProtocolError && ex.Response != null)
                        {
                            var resp = (HttpWebResponse)ex.Response;
                            if (resp.StatusCode == HttpStatusCode.NotFound) // HTTP 404
                            {
                                //the page was not found, continue with next in the for loop
                                movie.NetflixUrl = null;
                                continue;
                            }
                        }
                        //throw any other exception - this should not occur
                        throw;
                    }
                    if (r1.Contains("Stream on Netflix"))
                        break;
                    else
                        movie.NetflixUrl = null;
                }
                if (movie.NetflixUrl != null)
                    break;
            }
            */
            if (enableNetflix == "enabled")
                movie.NetflixUrl = null;
            if (enablePrimeVideo == "enabled")
                movie.PrimeVideoUrl = null;
            if (enableHoopla == "enabled")
                movie.HooplaUrl = null;
            if (enableTubi == "enabled")
                movie.TubiUrl = null;
            movie.JustWatchUrl = null;
            if (movie.Status == MovieStatusType.Released)
            {
                string api_url = "https://apis.justwatch.com/content/titles/" + locale + "/popular";
                foreach (var title in movie.AlternativeTitles)
                {
                    var payload = new JustWatchPayload();
                    payload.age_certifications = null;
                    payload.content_types = null;
                    payload.presentation_types = null;
                    payload.providers = null;
                    payload.genres = null;
                    payload.languages = null;
                    payload.release_year_from = movie.Year-1;
                    payload.release_year_until = movie.Year+1;
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
                        st.Write(byteArray, 0, byteArray.Length);
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
                                if (rsponseObject.items[i].scoring[j].value == movie.TmdbId)
                                {
                                    movie.JustWatchUrl = "https://www.justwatch.com" + rsponseObject.items[i].full_path;
                                    if (enableNetflix == "enabled" || enablePrimeVideo == "enabled" || enableHoopla == "enabled" || enableTubi == "enabled")
                                    {
                                        for (int k = 0; (rsponseObject.items[i].offers != null) && (k < rsponseObject.items[i].offers.Count); k++)
                                        {
                                            if (enableNetflix == "enabled" && rsponseObject.items[i].offers[k].urls.standard_web.Contains("http://www.netflix.com/title/"))
                                            {
                                                movie.NetflixUrl = rsponseObject.items[i].offers[k].urls.standard_web;
                                            }
                                            if (enablePrimeVideo == "enabled" && rsponseObject.items[i].offers[k].urls.standard_web.Contains("https://www.primevideo.com/detail/"))
                                            {
                                                movie.PrimeVideoUrl = rsponseObject.items[i].offers[k].urls.standard_web;
                                            }
                                            if (enableHoopla == "enabled" && rsponseObject.items[i].offers[k].urls.standard_web.Contains("https://www.hoopladigital.com/title/"))
                                            {
                                                movie.HooplaUrl = rsponseObject.items[i].offers[k].urls.standard_web;
                                            }
                                            if (enableTubi == "enabled" && rsponseObject.items[i].offers[k].urls.standard_web.Contains("https://tubitv.com/movies/"))
                                            {
                                                movie.TubiUrl = rsponseObject.items[i].offers[k].urls.standard_web;
                                            }
                                        }
                                    }
                                }
                                break;
                            }
                        }
                        
                    }
                    /*
                    movie.JustWatchUrl = "https://www.justwatch.com" + rsponseObject.items[i].full_path;
                    for (int k = 0; k < rsponseObject.items[i].offers.Count; k++)
                    {
                        if (rsponseObject.items[i].offers[k].urls.standard_web.Contains("http://www.netflix.com/title/"))
                        {
                            movie.NetflixUrl = rsponseObject.items[k].offers[j].urls.standard_web;
                        }
                        if (rsponseObject.items[i].offers[k].urls.standard_web.Contains("https://www.primevideo.com/detail/"))
                        {
                            movie.PrimeVideoUrl = rsponseObject.items[i].offers[k].urls.standard_web;
                        }
                    }
                    */

                    /* Old Code -- seemed to work but probably unreliable and not clean 
                    int start = rsponseString.IndexOf("/movie/" + ToUrlSlug(title.ToString(), true));
                    int end = -1;
                    if (start != -1)
                    {
                        end = rsponseString.Substring(start).IndexOf("\"tmdb:id\",\"value\":" + movie.TmdbId);
                    }
                    if (start != -1 && end != -1)
                    {
                        var data = rsponseString.Substring(start, end);
                        var start_netflix = data.IndexOf("http://www.netflix.com/title/");
                        if (start_netflix != -1)
                        {
                            var end_netflix = data.Substring(start_netflix).IndexOf(',');
                            movie.NetflixUrl = data.Substring(start_netflix, end_netflix - 1);
                        }
                        var start_prime = data.IndexOf("https://www.primevideo.com/detail/");
                        if (start_prime != -1)
                        {
                            var end_prime = data.Substring(start_prime).IndexOf(',');
                            movie.PrimeVideoUrl = data.Substring(start_prime, end_prime - 2);
                        }
                        break;
                    }
                    end of Old Code -- leaving in for reference*/
                }
            }
            return movie;
        }

        public Movie GetMovieInfo(string imdbId)
        {
            var request = _movieBuilder.Create()
                .SetSegment("route", "find")
                .SetSegment("id", imdbId)
                .SetSegment("secondaryRoute", "")
                .AddQueryParam("external_source", "imdb_id")
                .Build();

            request.AllowAutoRedirect = true;
            // request.SuppressHttpError = true;

            var response = _httpClient.Get<FindRoot>(request);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new HttpException(request, response);
            }

            if (response.Headers.ContentType != HttpAccept.JsonCharset.Value)
            {
                throw new HttpException(request, response);
            }

            // The dude abides, so should us, Lets be nice to TMDb
            // var allowed = int.Parse(response.Headers.GetValues("X-RateLimit-Limit").First()); // get allowed
            // var reset = long.Parse(response.Headers.GetValues("X-RateLimit-Reset").First()); // get time when it resets
            if (response.Headers.ContainsKey("X-RateLimit-Remaining"))
            {
                var remaining = int.Parse(response.Headers.GetValues("X-RateLimit-Remaining").First());
                if (remaining <= 5)
                {
                    _logger.Trace("Waiting 5 seconds to get information for the next 35 movies");
                    Thread.Sleep(5000);
                }
            }

            var resources = response.Resource;

            return resources.movie_results.SelectList(MapMovie).FirstOrDefault();
        }

        public List<Movie> DiscoverNewMovies(string action)
        {
            var allMovies = _movieService.GetAllMovies();
            var allExclusions = _exclusionService.GetAllExclusions();
            string allIds = string.Join(",", allMovies.Select(m => m.TmdbId));
            string ignoredIds = string.Join(",", allExclusions.Select(ex => ex.TmdbId));

            List<MovieResult> results = new List<MovieResult>();

            try
            {
                results = _radarrAPI.DiscoverMovies(action, (request) =>
                {
                    request.AllowAutoRedirect = true;
                    request.Method = HttpMethod.POST;
                    request.Headers.ContentType = "application/x-www-form-urlencoded";
                    request.SetContent($"tmdbIds={allIds}&ignoredIds={ignoredIds}");
                    return request;
                });

                results = results.Where(m => allMovies.None(mo => mo.TmdbId == m.id) && allExclusions.None(ex => ex.TmdbId == m.id)).ToList();
            }
            catch (RadarrAPIException exception)
            {
                _logger.Error(exception, "Failed to discover movies for action {0}!", action);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Failed to discover movies for action {0}!", action);
            }

            return results.SelectList(MapMovie);
        }

        private string StripTrailingTheFromTitle(string title)
        {
            if(title.EndsWith(",the"))
            {
                title = title.Substring(0, title.Length - 4);
            } else if(title.EndsWith(", the"))
            {
                title = title.Substring(0, title.Length - 5);
            }
            return title;
        }

        public List<Movie> SearchForNewMovie(string title)
        {
            var lowerTitle = title.ToLower();

            lowerTitle = lowerTitle.Replace(".", "");

            var parserResult = Parser.Parser.ParseMovieTitle(title, true, true);

            var yearTerm = "";

            if (parserResult != null && parserResult.MovieTitle != title)
            {
                //Parser found something interesting!
                lowerTitle = parserResult.MovieTitle.ToLower().Replace(".", " "); //TODO Update so not every period gets replaced (e.g. R.I.P.D.)
                if (parserResult.Year > 1800)
                {
                    yearTerm = parserResult.Year.ToString();
                }

                if (parserResult.ImdbId.IsNotNullOrWhiteSpace())
                {
                    try
                    {
                        return new List<Movie> { GetMovieInfo(parserResult.ImdbId) };
                    }
                    catch (Exception e)
                    {
                        return new List<Movie>();
                    }

                }
            }

            lowerTitle = StripTrailingTheFromTitle(lowerTitle);

            if (lowerTitle.StartsWith("imdb:") || lowerTitle.StartsWith("imdbid:"))
            {
                var slug = lowerTitle.Split(':')[1].Trim();

                string imdbid = slug;

                if (slug.IsNullOrWhiteSpace() || slug.Any(char.IsWhiteSpace))
                {
                    return new List<Movie>();
                }

                try
                {
                    return new List<Movie> { GetMovieInfo(imdbid) };
                }
                catch (MovieNotFoundException)
                {
                    return new List<Movie>();
                }
            }

            if (lowerTitle.StartsWith("tmdb:") || lowerTitle.StartsWith("tmdbid:"))
            {
                var slug = lowerTitle.Split(':')[1].Trim();

                int tmdbid = -1;

                if (slug.IsNullOrWhiteSpace() || slug.Any(char.IsWhiteSpace) || !(int.TryParse(slug, out tmdbid)))
                {
                    return new List<Movie>();
                }

                try
                {
                    return new List<Movie> { GetMovieInfo(tmdbid) };
                }
                catch (MovieNotFoundException)
                {
                    return new List<Movie>();
                }
            }

            var searchTerm = lowerTitle.Replace("_", "+").Replace(" ", "+").Replace(".", "+");

            var firstChar = searchTerm.First();

            var request = _movieBuilder.Create()
                .SetSegment("route", "search")
                .SetSegment("id", "movie")
                .SetSegment("secondaryRoute", "")
                .AddQueryParam("query", searchTerm)
                .AddQueryParam("year", yearTerm)
                .AddQueryParam("include_adult", false)
                .Build();

            request.AllowAutoRedirect = true;
            request.SuppressHttpError = true;

            /*var imdbRequest = new HttpRequest("https://v2.sg.media-imdb.com/suggests/" + firstChar + "/" + searchTerm + ".json");

            var response = _httpClient.Get(imdbRequest);

            var imdbCallback = "imdb$" + searchTerm + "(";

            var responseCleaned = response.Content.Replace(imdbCallback, "").TrimEnd(")");

            _logger.Warn("Cleaned response: " + responseCleaned);

            ImdbResource json =Json Convert.DeserializeObject<ImdbResource>(responseCleaned);

            _logger.Warn("Json object: " + json);

            _logger.Warn("Crash ahead.");*/

            var response = _httpClient.Get<MovieSearchRoot>(request);

            var movieResults = response.Resource.results;

            return movieResults.SelectList(MapMovie);
        }

        public Movie MapMovie(MovieResult result)
        {
            var imdbMovie = new Movie();
            imdbMovie.TmdbId = result.id;
            try
            {
                imdbMovie.SortTitle = Parser.Parser.NormalizeTitle(result.title);
                imdbMovie.Title = result.title;
                imdbMovie.TitleSlug = Parser.Parser.ToUrlSlug(result.title);

                try
                {
                    if (result.release_date.IsNotNullOrWhiteSpace())
                    {
                        imdbMovie.InCinemas = DateTime.Parse(result.release_date);
                        imdbMovie.Year = imdbMovie.InCinemas.Value.Year;
                    }

                    if (result.physical_release.IsNotNullOrWhiteSpace())
                    {
                        imdbMovie.PhysicalRelease = DateTime.Parse(result.physical_release);
                        if (result.physical_release_note.IsNotNullOrWhiteSpace())
                        {
                            imdbMovie.PhysicalReleaseNote = result.physical_release_note;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug("Not a valid date time.");
                }



                var now = DateTime.Now;
				//handle the case when we have both theatrical and physical release dates
				if (imdbMovie.InCinemas.HasValue && imdbMovie.PhysicalRelease.HasValue)
				{
					if (now < imdbMovie.InCinemas)
						imdbMovie.Status = MovieStatusType.Announced;
					else if (now >= imdbMovie.InCinemas)
						imdbMovie.Status = MovieStatusType.InCinemas;
					if (now >= imdbMovie.PhysicalRelease)
						imdbMovie.Status = MovieStatusType.Released;
				}
				//handle the case when we have theatrical release dates but we dont know the physical release date
				else if (imdbMovie.InCinemas.HasValue && (now >= imdbMovie.InCinemas))
				{
					imdbMovie.Status = MovieStatusType.InCinemas;
				}
				//handle the case where we only have a physical release date
				else if (imdbMovie.PhysicalRelease.HasValue && (now >= imdbMovie.PhysicalRelease))
				{
					imdbMovie.Status = MovieStatusType.Released;
				}
				//otherwise the title has only been announced
				else
				{
					imdbMovie.Status = MovieStatusType.Announced;
				}

				//since TMDB lacks alot of information lets assume that stuff is released if its been in cinemas for longer than 3 months.
				if (!imdbMovie.PhysicalRelease.HasValue && (imdbMovie.Status == MovieStatusType.InCinemas) && (((DateTime.Now).Subtract(imdbMovie.InCinemas.Value)).TotalSeconds > 60 * 60 * 24 * 30 * 3))
				{
					imdbMovie.Status = MovieStatusType.Released;
				}

                imdbMovie.TitleSlug += "-" + imdbMovie.TmdbId;

                imdbMovie.Images = new List<MediaCover.MediaCover>();
                imdbMovie.Overview = result.overview;
                imdbMovie.Ratings = new Ratings { Value = (decimal)result.vote_average, Votes = result.vote_count};

                try
                {
                    var imdbPoster = _configService.GetCoverForURL(result.poster_path, MediaCoverTypes.Poster);
                    imdbMovie.Images.Add(imdbPoster);
                }
                catch (Exception e)
                {
                    _logger.Debug(result);
                }

                if (result.trailer_key.IsNotNullOrWhiteSpace() && result.trailer_site.IsNotNullOrWhiteSpace())
                {
                    if (result.trailer_site == "youtube")
                    {
                        imdbMovie.YouTubeTrailerId = result.trailer_key;
                    }

                }

                return imdbMovie;
            }
            catch (Exception e)
            {
                _logger.Error(e, "Error occured while searching for new movies.");
            }

            return null;
        }

        private static Actor MapActors(ActorResource arg)
        {
            var newActor = new Actor
            {
                Name = arg.Name,
                Character = arg.Character
            };

            if (arg.Image != null)
            {
                newActor.Images = new List<MediaCover.MediaCover>
                {
                    new MediaCover.MediaCover(MediaCoverTypes.Headshot, arg.Image)
                };
            }

            return newActor;
        }

        private static Ratings MapRatings(RatingResource rating)
        {
            if (rating == null)
            {
                return new Ratings();
            }

            return new Ratings
            {
                Votes = rating.Count,
                Value = rating.Value
            };
        }

        private static MediaCover.MediaCover MapImage(ImageResource arg)
        {
            return new MediaCover.MediaCover
            {
                Url = arg.Url,
                CoverType = MapCoverType(arg.CoverType)
            };
        }

        private static MediaCoverTypes MapCoverType(string coverType)
        {
            switch (coverType.ToLower())
            {
                case "poster":
                    return MediaCoverTypes.Poster;
                case "banner":
                    return MediaCoverTypes.Banner;
                case "fanart":
                    return MediaCoverTypes.Fanart;
                default:
                    return MediaCoverTypes.Unknown;
            }
        }

        public static string getBetween(string strSource, string strStart, string strEnd)
        {
            int Start, End;
            if (strSource.Contains(strStart) && strSource.Contains(strEnd))
            {
                Start = strSource.IndexOf(strStart, 0) + strStart.Length;
                End = strSource.IndexOf(strEnd, Start);
                return strSource.Substring(Start, End - Start);
            }
            else
            {
                return "";
            }
        }

        public static string ToUrlSlug(string value, bool allflicks = false)
        {
            //First to lower case
            value = value.ToLowerInvariant();

            //Remove all accents
            var bytes = Encoding.GetEncoding("ISO-8859-8").GetBytes(value);
            value = Encoding.ASCII.GetString(bytes);

            //Replace spaces
            value = Regex.Replace(value, @"\s", "-", RegexOptions.Compiled);

            if (allflicks)
            {
                //Replace & with and
                value = Regex.Replace(value, @"&", "and", RegexOptions.Compiled);

                //Replace / with -
                value = Regex.Replace(value, @"/", "-", RegexOptions.Compiled);

                //Replace ' with -
                value = Regex.Replace(value, @"'", "-", RegexOptions.Compiled);
            }

            //Remove invalid chars
            value = Regex.Replace(value, @"[^a-z0-9\s-_]", "", RegexOptions.Compiled);

            //Trim dashes from end
            value = value.Trim('-', '_');

            //Replace double occurences of - or _
            value = Regex.Replace(value, @"([-_]){2,}", "$1", RegexOptions.Compiled);

            return value;
        }

        public Movie MapMovieToTmdbMovie(Movie movie)
        {
			try
			{
				 Movie newMovie = movie;
	            if (movie.TmdbId > 0)
	            {
	                newMovie = GetMovieInfo(movie.TmdbId);
	            }
	            else if (movie.ImdbId.IsNotNullOrWhiteSpace())
	            {
	                newMovie = GetMovieInfo(movie.ImdbId);
	            }
	            else
	            {
	                var yearStr = "";
	                if (movie.Year > 1900)
	                {
	                    yearStr = $" {movie.Year}";
	                }
	                newMovie = SearchForNewMovie(movie.Title + yearStr).FirstOrDefault();
	            }

	            if (newMovie == null)
	            {
	                _logger.Warn("Couldn't map movie {0} to a movie on The Movie DB. It will not be added :(", movie.Title);
	                return null;
	            }

	            newMovie.Path = movie.Path;
	            newMovie.RootFolderPath = movie.RootFolderPath;
	            newMovie.ProfileId = movie.ProfileId;
	            newMovie.Monitored = movie.Monitored;
	            newMovie.MovieFile = movie.MovieFile;
	            newMovie.MinimumAvailability = movie.MinimumAvailability;
	            newMovie.Tags = movie.Tags;

	            return newMovie;
			}
			catch (Exception ex)
			{
				_logger.Warn(ex, "Couldn't map movie {0} to a movie on The Movie DB. It will not be added :(", movie.Title);
	                return null;
			}
        }
    }
}
