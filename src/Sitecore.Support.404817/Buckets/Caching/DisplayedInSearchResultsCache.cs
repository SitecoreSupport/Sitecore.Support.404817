using Sitecore.Caching;
using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Data.Events;
using Sitecore.Data.Items;
using Sitecore.Data.Managers;
using Sitecore.Diagnostics;
using Sitecore.Events;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sitecore.Support.Buckets.Caching
{
    public class DisplayedInSearchResultsCache : Cache
    {
        private readonly string cacheName;

        private readonly List<ID> fieldIDsOfTemplate;

        public DisplayedInSearchResultsCache(string cacheName, List<ID> changedFieldIDsOfTemplate) : base(cacheName, Settings.Caching.DefaultDataCacheSize)
        {
            Assert.ArgumentNotNull(cacheName, "cacheName");
            Assert.ArgumentNotNull(changedFieldIDsOfTemplate, "changedFieldIDsOfTemplate");
            this.cacheName = cacheName;
            this.fieldIDsOfTemplate = changedFieldIDsOfTemplate;
            Event.Subscribe("item:saved", new EventHandler(this.ItemSaved));
            Event.Subscribe("item:saved:remote", new EventHandler(this.ItemSavedRemote));
        }

        private void ItemSavedRemote(object sender, EventArgs e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(e, "e");
            ItemSavedRemoteEventArgs itemSavedRemoteEventArgs = e as ItemSavedRemoteEventArgs;
            if (itemSavedRemoteEventArgs != null)
            {
                this.StartProcess(itemSavedRemoteEventArgs.Changes);
            }
        }

        private void ItemSaved(object sender, EventArgs e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(e, "e");
            Assert.ArgumentNotNull(e, "e");
            if (e is SitecoreEventArgs)
            {
                ItemChanges itemChanges = Event.ExtractParameter<ItemChanges>(e, 1);
                this.StartProcess(itemChanges);
            }
        }

        private void StartProcess(ItemChanges itemChanges)
        {
            Assert.ArgumentNotNull(itemChanges, "itemChanges");
            if (this.ShouldClearCache(itemChanges))
            {
                this.ClearCache();
            }
        }

        private bool ShouldClearCache(ItemChanges itemChanges)
        {
            Assert.ArgumentNotNull(itemChanges, "itemChanges");
            return TemplateManager.IsTemplatePart(itemChanges.Item) && this.fieldIDsOfTemplate.Any((ID fieldId) => itemChanges.IsFieldModified(fieldId));
        }

        private void ClearCache()
        {
            Cache cache = CacheManager.FindCacheByName(this.cacheName);
            if (cache != null)
            {
                cache.Clear();
            }
        }
    }
}
