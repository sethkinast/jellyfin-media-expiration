// Web/detailbadge.js — injected into the Jellyfin SPA
(function () {
    'use strict';

    const API_BASE = '/MediaExpiration/Item/';

    function getToken() {
        // Jellyfin stores the access token in localStorage
        try {
            const creds = JSON.parse(localStorage.getItem('jellyfin_credentials') || '{}');
            return creds?.Servers?.[0]?.AccessToken ?? '';
        } catch { return ''; }
    }

    async function fetchExpiration(itemId) {
        try {
            const res = await fetch(`${API_BASE}${itemId}`, {
                headers: { 'X-Emby-Token': getToken() }
            });
            if (!res.ok) return null;
            return await res.json();
        } catch { return null; }
    }

    function formatBadge(info) {
        if (info.daysRemaining < 0) {
            return { text: 'Expired', color: '#c0392b' };
        } else if (info.daysRemaining === 0) {
            return { text: 'Expires today', color: '#e67e22' };
        } else if (info.daysRemaining <= 7) {
            return { text: `Expires in ${info.daysRemaining}d`, color: '#e67e22' };
        } else {
            const date = new Date(info.expiresAt).toLocaleDateString();
            return { text: `Expires ${date}`, color: '#7f8c8d' };
        }
    }

    function injectBadge(info) {
        // Remove any existing badge from a prior navigation
        document.getElementById('media-expiration-badge')?.remove();

        const { text, color } = formatBadge(info);

        const badge = document.createElement('div');
        badge.id = 'media-expiration-badge';
        badge.dataset.itemId = info.itemId;
        badge.style.cssText = `
            display: inline-block;
            margin: 4px 8px 0 0;
            padding: 3px 10px;
            border-radius: 4px;
            background: ${color};
            color: #fff;
            font-size: 0.8em;
            font-weight: 600;
            letter-spacing: 0.03em;
            vertical-align: middle;
        `;
        badge.textContent = text;

        // The detail page action buttons row is a stable anchor point
        const anchor = document.querySelector('.detailPageContent .actionButtons')
            ?? document.querySelector('.itemDetailPage .mainDetailButtons')
            ?? document.querySelector('.detailRibbon');

        if (anchor) {
            anchor.prepend(badge);
        }
    }

    function getItemIdFromUrl() {
        // Detail page URLs are like /#!/details?id=abc123 or /web/#/details?id=abc123&...
        const params = new URLSearchParams(
            window.location.hash.includes('?')
                ? window.location.hash.split('?')[1]
                : window.location.search
        );
        return params.get('id');
    }

    // Cache the last fetch so we don't re-request for the same item
    let lastItemId = null;
    let lastInfo = null;
    let fetchInFlight = null;

    function findAnchor() {
        return document.querySelector('.detailPageContent .actionButtons')
            ?? document.querySelector('.itemDetailPage .mainDetailButtons')
            ?? document.querySelector('.detailRibbon');
    }

    async function tryInject() {
        const hash = window.location.hash;
        if (!hash.includes('details')) {
            lastItemId = null;
            lastInfo = null;
            document.getElementById('media-expiration-badge')?.remove();
            return;
        }

        const itemId = getItemIdFromUrl();
        if (!itemId) return;

        const anchor = findAnchor();
        if (!anchor) return;

        // Badge already present for this item — nothing to do
        const existing = document.getElementById('media-expiration-badge');
        if (existing && existing.dataset.itemId === itemId) return;

        // Remove stale badge from a different item
        existing?.remove();

        // Fetch if needed (or reuse cache)
        if (itemId !== lastItemId) {
            // Avoid duplicate in-flight requests for the same item
            if (fetchInFlight === itemId) return;
            fetchInFlight = itemId;
            const info = await fetchExpiration(itemId);
            fetchInFlight = null;
            lastItemId = itemId;
            lastInfo = info;
        }

        if (!lastInfo) {
            // Item is not/no longer expiring — remove any stale badge
            document.getElementById('media-expiration-badge')?.remove();
            return;
        }

        // Re-check anchor — DOM may have changed during the await
        if (findAnchor() && !document.getElementById('media-expiration-badge')) {
            injectBadge(lastInfo);
        }
    }

    function invalidateAndRefresh() {
        lastItemId = null;
        lastInfo = null;
        tryInject();
    }

    // Watch for DOM changes so we catch any navigation method and re-inject
    // if Jellyfin rebuilds the page after us.
    let debounceTimer = null;
    const observer = new MutationObserver(() => {
        if (debounceTimer) return;
        debounceTimer = setTimeout(() => {
            debounceTimer = null;
            tryInject();
        }, 150);
    });
    observer.observe(document.body, { childList: true, subtree: true });

    // Also trigger on explicit navigation events for faster response
    window.addEventListener('hashchange', tryInject);
    window.addEventListener('popstate', tryInject);

    // Refresh badge when the user toggles favorite
    document.addEventListener('click', (e) => {
        const btn = e.target.closest('.emby-ratingbutton, .btnUserItemRating, [data-action="setfavorite"]');
        if (btn) {
            // Wait for the Jellyfin API call to complete, then re-fetch
            setTimeout(invalidateAndRefresh, 600);
        }
    });

    tryInject();
})();