import React, { useState, useEffect, useRef } from 'react';
import { urlService } from './services/urlService';
import type { ShortUrl } from './types';

export default function App() {
  const [urls, setUrls] = useState<ShortUrl[]>([]);
  const [filteredUrls, setFilteredUrls] = useState<ShortUrl[]>([]);
  const [originalUrl, setOriginalUrl] = useState('');
  const [isPrivate, setIsPrivate] = useState(false);
  const [searchQuery, setSearchQuery] = useState('');

  // UI States
  const [isLoading, setIsLoading] = useState(false);
  const [isActionLoading, setIsActionLoading] = useState(false);
  const [errorMessage, setErrorMessage] = useState('');
  const [toastMessage, setToastMessage] = useState('');
  const [showToast, setShowToast] = useState(false);
  const [privateLinkGenerated, setPrivateLinkGenerated] = useState<ShortUrl | null>(null);

  // Statistics
  const [totalClicks, setTotalClicks] = useState(0);
  const [totalUrlsCount, setTotalUrlsCount] = useState(0);

  // Timeout Refs for auto-hiding banners and toasts
  const toastTimeoutRef = useRef<any>(null);
  const errorTimeoutRef = useRef<any>(null);

  // Load public shortened URLs on mount
  useEffect(() => {
    loadUrls();
    return () => {
      if (toastTimeoutRef.current) clearTimeout(toastTimeoutRef.current);
      if (errorTimeoutRef.current) clearTimeout(errorTimeoutRef.current);
    };
  }, []);

  // Recalculate stats whenever URLs list changes
  useEffect(() => {
    calculateStats(urls);
  }, [urls]);

  // Fetch URLs from API
  const loadUrls = async (query?: string) => {
    setIsLoading(true);
    try {
      const data = await urlService.getUrls(query);
      setUrls(data);
      setFilteredUrls(data);
    } catch (err: any) {
      console.error('Error fetching URLs:', err);
      showErrorBanner('Failed to load shortened URLs. Make sure the backend API is running.');
    } finally {
      setIsLoading(false);
    }
  };

  // Stats calculation
  const calculateStats = (items: ShortUrl[]) => {
    setTotalUrlsCount(items.length);
    const clicks = items.reduce((sum, curr) => sum + curr.totalClicks, 0);
    setTotalClicks(clicks);
  };

  // Show a temporary Toast notification
  const triggerToast = (message: string) => {
    if (toastTimeoutRef.current) clearTimeout(toastTimeoutRef.current);
    setToastMessage(message);
    setShowToast(true);
    toastTimeoutRef.current = setTimeout(() => {
      setShowToast(false);
    }, 3500);
  };

  // Show error banner
  const showErrorBanner = (msg: string) => {
    if (errorTimeoutRef.current) clearTimeout(errorTimeoutRef.current);
    setErrorMessage(msg);
    errorTimeoutRef.current = setTimeout(() => {
      setErrorMessage('');
    }, 6000);
  };

  // Submit creator form
  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setErrorMessage('');

    if (!originalUrl || !originalUrl.trim()) {
      showErrorBanner('Please enter a valid URL.');
      return;
    }

    let urlToSubmit = originalUrl.trim();

    // Auto-prepend http if protocol is completely missing
    if (!/^https?:\/\//i.test(urlToSubmit)) {
      urlToSubmit = 'http://' + urlToSubmit;
    }

    try {
      // Basic client-side URL validation check
      new URL(urlToSubmit);
    } catch (_) {
      showErrorBanner('Please enter a valid absolute URL (e.g. https://google.com).');
      return;
    }

    setIsActionLoading(true);
    try {
      const newUrl = await urlService.shortenUrl(urlToSubmit, isPrivate);
      setOriginalUrl('');
      const wasPrivate = isPrivate;
      setIsPrivate(false); // Reset toggle

      if (wasPrivate) {
        // Private Link created - doesn't display in list, show secure window details
        triggerToast('Private Link created successfully!');
        setPrivateLinkGenerated(newUrl);
      } else {
        // Public link created, prepend to top of list
        const updated = [newUrl, ...urls];
        setUrls(updated);
        
        // Refresh filter logic directly
        applyFilter(searchQuery, updated);
        triggerToast('Short URL created successfully!');
      }
    } catch (err: any) {
      console.error('Error shortening URL:', err);
      const errMsg = err.message || 'Failed to shorten URL. Make sure the API is online and the URL format is correct.';
      showErrorBanner(errMsg);
    } finally {
      setIsActionLoading(false);
    }
  };

  // Delete a shortened URL entry
  const onDelete = async (urlItem: ShortUrl) => {
    if (!window.confirm(`Are you sure you want to delete the short URL for ${urlItem.originalURL}?`)) {
      return;
    }

    try {
      await urlService.deleteUrl(urlItem.code);
      const updated = urls.filter((u) => u.code !== urlItem.code);
      setUrls(updated);
      applyFilter(searchQuery, updated);
      triggerToast('Short URL deleted successfully.');
    } catch (err) {
      console.error('Error deleting URL:', err);
      triggerToast('Error: Failed to delete short URL.');
    }
  };

  // Search filter implementation
  const applyFilter = (query: string, currentUrls: ShortUrl[]) => {
    if (!query || !query.trim()) {
      setFilteredUrls(currentUrls);
    } else {
      const q = query.toLowerCase().trim();
      const filtered = currentUrls.filter(
        (u) =>
          u.originalURL.toLowerCase().includes(q) ||
          u.code.toLowerCase().includes(q)
      );
      setFilteredUrls(filtered);
    }
  };

  const onSearchChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const query = e.target.value;
    setSearchQuery(query);
    applyFilter(query, urls);
  };

  // Copy link to clipboard
  const copyToClipboard = (urlStr: string) => {
    navigator.clipboard
      .writeText(urlStr)
      .then(() => {
        triggerToast('Shortened link copied to clipboard!');
      })
      .catch((err) => {
        console.error('Failed to copy text:', err);
        triggerToast('Failed to copy link.');
      });
  };

  const closePrivateLinkInfo = () => {
    setPrivateLinkGenerated(null);
    loadUrls(searchQuery); // Refresh database counts
  };

  // Nicely format Date like "May 30, 2026"
  const formatDate = (dateStr: string) => {
    try {
      const date = new Date(dateStr);
      return new Intl.DateTimeFormat('en-US', {
        month: 'short',
        day: 'numeric',
        year: 'numeric',
      }).format(date);
    } catch (_) {
      return dateStr;
    }
  };

  return (
    <div className="dashboard-wrapper">
      {/* Top Animated Background Gradients */}
      <div className="glow-sphere glow-sphere-1"></div>
      <div className="glow-sphere glow-sphere-2"></div>

      {/* Header Section */}
      <header className="dashboard-header animate-fade-in">
        <div className="logo-container">
          <span className="logo-text">
            TINY<span className="logo-dot">.</span>URL
          </span>
        </div>
        <p className="subtitle">
          A premium, secure URL shortener offering instant redirections and real-time click statistics.
        </p>
      </header>

      {/* Main Creator Section */}
      <section className="creator-section animate-slide-up">
        <div className="glass-card creator-card">
          <h2 className="card-title">Shorten Your Destination Link</h2>

          <form onSubmit={onSubmit} className="shorten-form">
            <div className="input-wrapper">
              <span className="input-icon">🔗</span>
              <input
                type="text"
                value={originalUrl}
                onChange={(e) => setOriginalUrl(e.target.value)}
                placeholder="Paste your long destination URL here..."
                required
                className="url-input"
                disabled={isActionLoading}
              />
            </div>

            <div className="form-controls">
              <label
                className="privacy-toggle"
                title="Private URLs redirect successfully but will not show in the dashboard below."
              >
                <input
                  type="checkbox"
                  checked={isPrivate}
                  onChange={(e) => setIsPrivate(e.target.checked)}
                  className="toggle-checkbox"
                />
                <span className="toggle-slider"></span>
                <span className="toggle-label">
                  Make it Private <span className="lock-icon">🔒</span>
                </span>
              </label>

              <button type="submit" className="submit-btn" disabled={isActionLoading}>
                {!isActionLoading ? (
                  <span>Shorten URL</span>
                ) : (
                  <span className="loader-spinner"></span>
                )}
              </button>
            </div>
          </form>

          {/* Client-side / API Errors */}
          {errorMessage && (
            <div className="error-banner animate-shake">
              <span className="error-icon">⚠️</span>
              <span className="error-text">{errorMessage}</span>
            </div>
          )}
        </div>
      </section>

      {/* Statistics Panel */}
      {!isLoading && (
        <section className="stats-section animate-slide-up-delay-1">
          <div className="glass-card stats-card">
            <div className="stats-icon clicks-icon">📈</div>
            <div className="stats-data">
              <span className="stats-num">{totalClicks}</span>
              <span className="stats-label">Total Redirect Clicks</span>
            </div>
          </div>
          <div className="glass-card stats-card">
            <div className="stats-icon urls-icon">🔗</div>
            <div className="stats-data">
              <span className="stats-num">{totalUrlsCount}</span>
              <span className="stats-label">Active Public Links</span>
            </div>
          </div>
        </section>
      )}

      {/* Links Table / Listing Section */}
      <section className="links-section animate-slide-up-delay-2">
        <div className="glass-card listing-card">
          <div className="listing-header">
            <h3 className="section-title">Active Redirection Dashboard</h3>

            {/* Search bar */}
            <div className="search-wrapper">
              <span className="search-icon">🔍</span>
              <input
                type="text"
                value={searchQuery}
                onChange={onSearchChange}
                placeholder="Search links..."
                className="search-input"
              />
            </div>
          </div>

          {/* Loading State */}
          {isLoading && (
            <div className="loading-state">
              <div className="pulsing-loader"></div>
              <p>Retrieving active link mappings...</p>
            </div>
          )}

          {/* Empty State */}
          {!isLoading && filteredUrls.length === 0 && (
            <div className="empty-state">
              <div className="empty-icon">📂</div>
              <p className="empty-title">No public short URLs found</p>
              <p className="empty-subtitle">Create a public URL above or try refining your search query.</p>
            </div>
          )}

          {/* Links Grid Table */}
          {!isLoading && filteredUrls.length > 0 && (
            <div className="table-container">
              <table className="links-table">
                <thead>
                  <tr>
                    <th>Original Link</th>
                    <th>Short Link</th>
                    <th className="text-center">Clicks</th>
                    <th>Created Date</th>
                    <th className="text-right">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredUrls.map((url) => (
                    <tr key={url.code} className="table-row">
                      <td className="col-original">
                        <div className="original-link-wrapper" title={url.originalURL}>
                          <span className="globe-icon">🌐</span>
                          <span className="url-text">{url.originalURL}</span>
                        </div>
                      </td>
                      <td className="col-short">
                        <div className="short-link-wrapper">
                          <a
                            href={url.shortURL}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="short-link"
                          >
                            {url.shortURL}
                          </a>
                          <button
                            onClick={() => copyToClipboard(url.shortURL)}
                            className="icon-btn copy-btn"
                            title="Copy to clipboard"
                          >
                            📋
                          </button>
                        </div>
                      </td>
                      <td className="col-clicks text-center">
                        <span className="clicks-badge">{url.totalClicks}</span>
                      </td>
                      <td className="col-date">
                        <span className="date-text">{formatDate(url.createdAt)}</span>
                      </td>
                      <td className="col-actions text-right">
                        <button
                          onClick={() => onDelete(url)}
                          className="icon-btn delete-btn"
                          title="Delete Short URL"
                        >
                          🗑️
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </section>

      {/* Modal Backdrop & Popup for Private URL Generation */}
      {privateLinkGenerated && (
        <div className="modal-backdrop animate-fade-in">
          <div className="glass-card modal-card animate-zoom-in">
            <div className="modal-header">
              <span className="modal-lock-icon">🔒</span>
              <h3 className="modal-title">Private Link Generated!</h3>
            </div>
            <p className="modal-body">
              This short link is <strong>private</strong> and will not be displayed on the dashboard grid. Make sure to copy and save it now:
            </p>

            <div className="modal-link-box">
              <span className="modal-short-link">{privateLinkGenerated.shortURL}</span>
              <button
                onClick={() => copyToClipboard(privateLinkGenerated.shortURL)}
                className="modal-copy-btn"
              >
                Copy Link
              </button>
            </div>

            <div className="modal-details">
              <p>
                <strong>Original Destination:</strong> {privateLinkGenerated.originalURL}
              </p>
              <p>
                <strong>Status:</strong> Active & Secure
              </p>
            </div>

            <button onClick={closePrivateLinkInfo} className="modal-close-btn">
              Close Secure Window
            </button>
          </div>
        </div>
      )}

      {/* Dynamic Premium Toast Notification */}
      <div className={`toast-notification ${showToast ? 'show' : ''}`}>
        <span className="toast-icon">✨</span>
        <span className="toast-text">{toastMessage}</span>
      </div>
    </div>
  );
}
