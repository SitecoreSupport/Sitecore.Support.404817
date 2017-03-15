using Newtonsoft.Json;
using Sitecore.Buckets.Extensions;
using Sitecore.Buckets.Pipelines.Search.GetFacets;
using Sitecore.Buckets.Pipelines.UI.FetchContextData;
using Sitecore.Buckets.Pipelines.UI.FetchContextView;
using Sitecore.Buckets.Pipelines.UI.FillItem;
using Sitecore.Buckets.Pipelines.UI.Search;
using Sitecore.Buckets.Search;
using Sitecore.Buckets.Util;
using Sitecore.Caching;
using Sitecore.Configuration;
using Sitecore.ContentSearch;
using Sitecore.ContentSearch.Diagnostics;
using Sitecore.ContentSearch.Linq;
using Sitecore.ContentSearch.Linq.Common;
using Sitecore.ContentSearch.Linq.Parsing;
using Sitecore.ContentSearch.SearchTypes;
using Sitecore.ContentSearch.Security;
using Sitecore.Data;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.Globalization;
using Sitecore.Support.Buckets.Caching;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.SessionState;

namespace Sitecore.Support.ItemBuckets.Services
{
    [UsedImplicitly]
    public class Search : SearchHttpTaskAsyncHandler, IRequiresSessionState
    {
        private static readonly object ThisLock = new object();
        private static volatile Hashtable cacheHashtable;

        public override bool IsReusable
        {
            get
            {
                return false;
            }
        }

        private static Hashtable CacheHashTable
        {
            get
            {
                if (Sitecore.Support.ItemBuckets.Services.Search.cacheHashtable == null)
                {
                    lock (Sitecore.Support.ItemBuckets.Services.Search.ThisLock)
                    {
                        if (Sitecore.Support.ItemBuckets.Services.Search.cacheHashtable == null)
                            Sitecore.Support.ItemBuckets.Services.Search.cacheHashtable = new Hashtable();
                    }
                }
                return Sitecore.Support.ItemBuckets.Services.Search.cacheHashtable;
            }
        }

        public override void ProcessRequest(HttpContext context)
        {
        }

        public override async Task ProcessRequestAsync(HttpContext context)
        {
            if (!Context.User.IsAuthenticated)
                this.CheckSecurity();
            context.Response.ContentType = "application/json";
            context.Response.ContentEncoding = Encoding.UTF8;
            this.Stopwatch = new Stopwatch();
            this.ItemsPerPage = BucketConfigurationSettings.DefaultNumberOfResultsPerPage;
            this.ExtractSearchQuery(context.Request.QueryString);
            bool flag = MainUtil.GetBool(Sitecore.ContentSearch.Utilities.SearchHelper.GetDebug(this.SearchQuery), false);
            if (flag && !BucketConfigurationSettings.EnableBucketDebug)
                Sitecore.Buckets.Util.Constants.EnableTemporaryBucketDebug = true;
            Database database = this.Database.IsNullOrEmpty() ? Context.ContentDatabase : Factory.GetDatabase(this.Database);
            if (!this.RunFacet)
            {
                this.StoreUserContextSearches();
                this.ItemsPerPage = Sitecore.ContentSearch.Utilities.SearchHelper.GetPageSize(this.SearchQuery).IsNumeric() ? int.Parse(Sitecore.ContentSearch.Utilities.SearchHelper.GetPageSize(this.SearchQuery)) : BucketConfigurationSettings.DefaultNumberOfResultsPerPage;
                SitecoreIndexableItem sitecoreIndexableItem = (SitecoreIndexableItem)(database.GetItem(this.LocationFilter) ?? database.GetRootItem());
                using (IProviderSearchContext searchContext1 = (this.IndexName.IsEmpty() ? ContentSearchManager.GetIndex((IIndexable)sitecoreIndexableItem) : ContentSearchManager.GetIndex(this.IndexName)).CreateSearchContext(SearchSecurityOptions.EnableSecurityCheck))
                {
                    UISearchArgs args = new UISearchArgs(searchContext1, (IEnumerable<Sitecore.ContentSearch.Utilities.SearchStringModel>)this.SearchQuery, sitecoreIndexableItem)
                    {
                        Page = int.Parse(this.PageNumber) - 1,
                        PageSize = this.ItemsPerPage
                    };
                    this.Stopwatch.Start();
                    IQueryable<UISearchResult> source1 = UISearchPipeline.Run(args);
                    SearchResults<UISearchResult> results = source1.GetResults<UISearchResult>();
                    IEnumerable<UISearchResult> source2 = results.Hits.Select<SearchHit<UISearchResult>, UISearchResult>((Func<SearchHit<UISearchResult>, UISearchResult>)(h => h.Document));
                    if (BucketConfigurationSettings.EnableBucketDebug || Sitecore.Buckets.Util.Constants.EnableTemporaryBucketDebug)
                    {
                        SearchLog.Log.Info(string.Format("Search Query : {0}", ((IHasNativeQuery)source1).Query), (Exception)null);
                        SearchLog.Log.Info(string.Format("Search Index : {0}", (object)ContentSearchManager.GetIndex((IIndexable)(SitecoreIndexableItem)database.GetItem(this.LocationFilter)).Name), (Exception)null);
                    }
                    List<UISearchResult> uiSearchResultList = source2.ToList<UISearchResult>();
                    int totalSearchResults = results.TotalSearchResults;
                    int num1 = totalSearchResults % this.ItemsPerPage == 0 ? totalSearchResults / this.ItemsPerPage : totalSearchResults / this.ItemsPerPage + 1;
                    int num2 = int.Parse(this.PageNumber);
                    if ((num2 - 1) * this.ItemsPerPage >= totalSearchResults)
                        num2 = 1;
                    List<TemplateFieldItem> templateFields = new List<TemplateFieldItem>();
                    if (source2 != null && Context.ContentDatabase != null)
                    {
                        using (IProviderSearchContext searchContext2 = ContentSearchManager.GetIndex((IIndexable)(SitecoreIndexableItem)Context.ContentDatabase.GetItem(ItemIDs.TemplateRoot)).CreateSearchContext(SearchSecurityOptions.EnableSecurityCheck))
                        {
                            IEnumerable<Tuple<string, string, string>> tuples = Sitecore.Support.ItemBuckets.Services.Search.ProcessCachedDisplayedSearch(sitecoreIndexableItem, searchContext2);
                            ItemCache itemCache = CacheManager.GetItemCache(Context.ContentDatabase);
                            foreach (Tuple<string, string, string> tuple in tuples)
                            {
                                Language result;
                                Sitecore.Globalization.Language.TryParse(tuple.Item2, out result);
                                Item ownerItem = itemCache.GetItem(new ID(tuple.Item1), result, new Sitecore.Data.Version(tuple.Item3));
                                if (ownerItem == null)
                                {
                                    ownerItem = Context.ContentDatabase.GetItem(new ID(tuple.Item1), result, new Sitecore.Data.Version(tuple.Item3));
                                    if (ownerItem != null)
                                        CacheManager.GetItemCache(Context.ContentDatabase).AddItem(ownerItem.ID, result, ownerItem.Version, ownerItem);
                                }
                                if (ownerItem != null && !templateFields.Contains(FieldTypeManager.GetTemplateFieldItem(new Field(ownerItem.ID, ownerItem))))
                                    templateFields.Add(FieldTypeManager.GetTemplateFieldItem(new Field(ownerItem.ID, ownerItem)));
                            }
                            uiSearchResultList = FillItemPipeline.Run(new FillItemArgs(templateFields, uiSearchResultList, this.Language));
                        }
                    }
                    if (this.IndexName == string.Empty)
                        uiSearchResultList = uiSearchResultList.RemoveWhere((UISearchResult item) => item.Name == null || item.Content == null).ToList();
                    if (!BucketConfigurationSettings.SecuredItems.Equals("hide", StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (totalSearchResults > BucketConfigurationSettings.DefaultNumberOfResultsPerPage && uiSearchResultList.Count < BucketConfigurationSettings.DefaultNumberOfResultsPerPage && num2 <= num1)
                        {
                            while (uiSearchResultList.Count < BucketConfigurationSettings.DefaultNumberOfResultsPerPage)
                                uiSearchResultList.Add(new UISearchResult()
                                {
                                    ItemId = Guid.NewGuid().ToString()
                                });
                        }
                        else if (uiSearchResultList.Count < totalSearchResults && num2 == 1)
                        {
                            while (uiSearchResultList.Count < totalSearchResults && totalSearchResults < BucketConfigurationSettings.DefaultNumberOfResultsPerPage)
                                uiSearchResultList.Add(new UISearchResult()
                                {
                                    ItemId = Guid.NewGuid().ToString()
                                });
                        }
                    }
                    this.Stopwatch.Stop();
                    IEnumerable<Tuple<View, object>> tuples1 = FetchContextDataPipeline.Run(new FetchContextDataArgs((IEnumerable<Sitecore.ContentSearch.Utilities.SearchStringModel>)this.SearchQuery, searchContext1, (IIndexable)sitecoreIndexableItem));
                    IEnumerable<Tuple<int, View, string, IEnumerable<UISearchResult>>> tuples2 = FetchContextViewPipeline.Run(new FetchContextViewArgs((IEnumerable<Sitecore.ContentSearch.Utilities.SearchStringModel>)this.SearchQuery, searchContext1, (IIndexable)sitecoreIndexableItem, (IEnumerable<TemplateFieldItem>)templateFields));
                    string callback = this.Callback;
                    string str1 = "(";
                    string str2 = JsonConvert.SerializeObject((object)new FullSearch()
                    {
                        PageNumbers = num1,
                        items = (IEnumerable<UISearchResult>)uiSearchResultList,
                        launchType = SearchHttpTaskAsyncHandler.GetEditorLaunchType(),
                        SearchTime = this.SearchTime,
                        SearchCount = totalSearchResults.ToString(),
                        ContextData = tuples1,
                        ContextDataView = tuples2,
                        CurrentPage = num2,
                        Location = (Context.ContentDatabase.GetItem(this.LocationFilter) != null ? Context.ContentDatabase.GetItem(this.LocationFilter).Name : Translate.Text("current item"))
                    });
                    string str3 = ")";
                    context.Response.Write(callback + str1 + str2 + str3);
                    if (!BucketConfigurationSettings.EnableBucketDebug)
                    {
                        if (!Sitecore.Buckets.Util.Constants.EnableTemporaryBucketDebug)
                            goto label_43;
                    }
                    SearchLog.Log.Info("Search Took : " + (object)this.Stopwatch.ElapsedMilliseconds + "ms", (Exception)null);
                }
            }
            else
            {
                string callback = this.Callback;
                string str1 = "(";
                string str2 = JsonConvert.SerializeObject((object)new FullSearch()
                {
                    PageNumbers = 1,
                    facets = GetFacetsPipeline.Run(new GetFacetsArgs(this.SearchQuery, this.LocationFilter)),
                    SearchCount = "1",
                    CurrentPage = 1
                });
                string str3 = ")";
                context.Response.Write(callback + str1 + str2 + str3);
            }
            label_43:
            if (!flag)
                return;
            Sitecore.Buckets.Util.Constants.EnableTemporaryBucketDebug = false;
        }

        private static IEnumerable<Tuple<string, string, string>> ProcessCachedDisplayedSearch(SitecoreIndexableItem startLocationItem, IProviderSearchContext searchContext)
        {
            string cacheName = "IsDisplayedInSearchResults[" + Context.ContentDatabase.Name + "]";
            Cache cache = (Cache)Sitecore.Support.ItemBuckets.Services.Search.CacheHashTable[(object)cacheName];
            IEnumerable<Tuple<string, string, string>> tuples = cache != null ? cache.GetValue((object)"cachedIsDisplayedSearch") as IEnumerable<Tuple<string, string, string>> : (IEnumerable<Tuple<string, string, string>>)null;
            if (tuples == null)
            {
                CultureInfo culture = startLocationItem != null ? startLocationItem.Culture : new CultureInfo(Settings.DefaultLanguage);
                tuples = (IEnumerable<Tuple<string, string, string>>)searchContext.GetQueryable<SitecoreUISearchResultItem>((IExecutionContext)new CultureExecutionContext(culture)).Where<SitecoreUISearchResultItem>((Expression<Func<SitecoreUISearchResultItem, bool>>)(templateField => templateField["Is Displayed in Search Results".ToLowerInvariant()] == "1")).ToList<SitecoreUISearchResultItem>().ConvertAll<Tuple<string, string, string>>((Converter<SitecoreUISearchResultItem, Tuple<string, string, string>>)(d => new Tuple<string, string, string>(d.GetItem().ID.ToString(), d.Language, d.Version)));
                if (Sitecore.Support.ItemBuckets.Services.Search.CacheHashTable[(object)cacheName] == null)
                {
                    lock (Sitecore.Support.ItemBuckets.Services.Search.CacheHashTable.SyncRoot)
                    {
                        if (Sitecore.Support.ItemBuckets.Services.Search.CacheHashTable[(object)cacheName] == null)
                        {
                            cache = (Cache)new DisplayedInSearchResultsCache(cacheName, new List<ID>()
              {
                new ID(Sitecore.Buckets.Util.Constants.IsDisplayedInSearchResults)
              });
                            Sitecore.Support.ItemBuckets.Services.Search.cacheHashtable[(object)cacheName] = (object)cache;
                        }
                    }
                }
                cache.Add("cachedIsDisplayedSearch", (object)tuples, Settings.Caching.DefaultFilteredItemsCacheSize);
            }
            return tuples;
        }
    }
}
