import type { ShortUrl, ShortenUrlRequest } from '../types';

// =========================================================================
// FRONTEND RUN COMMANDS:
// 1. Open Terminal, go to frontend folder: cd d:\Task\frontend
// 2. Set Node.js path (if needed): $env:Path = "C:\Program Files\nodejs;" + $env:Path
// 3. Launch React server: npm run dev (opens dashboard at http://localhost:5173/)
//
// Note: BASE_URL represents your running C# backend Web API root address.
// =========================================================================
const BASE_URL = import.meta.env.VITE_API_URL || 'https://localhost:44386';

export const urlService = {
  // Fetch all public URLs (optionally filtered by search query parameter)
  async getUrls(search?: string): Promise<ShortUrl[]> {
    let url = `${BASE_URL}/api/public`;
    if (search) {
      url += `?search=${encodeURIComponent(search)}`;
    }
    const response = await fetch(url);
    if (!response.ok) {
      const errorData = await response.json().catch(() => ({}));
      throw new Error(errorData.error || 'Failed to fetch public URLs.');
    }
    return response.json();
  },

  // Create a new shortened URL matching Swagger /api/add route
  async shortenUrl(originalURL: string, isPrivate: boolean): Promise<ShortUrl> {
    const request: ShortenUrlRequest = { originalURL, isPrivate };
    const response = await fetch(`${BASE_URL}/api/add`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
    });
    if (!response.ok) {
      const errorData = await response.json().catch(() => ({}));
      throw new Error(errorData.error || 'Failed to shorten URL.');
    }
    return response.json();
  },

  // Delete a shortened URL entry matching Swagger /api/delete/{code} route
  async deleteUrl(code: string): Promise<{ message: string }> {
    const response = await fetch(`${BASE_URL}/api/delete/${code}`, {
      method: 'DELETE',
    });
    if (!response.ok) {
      const errorData = await response.json().catch(() => ({}));
      throw new Error(errorData.error || 'Failed to delete shortened URL.');
    }
    return response.json();
  },
};
