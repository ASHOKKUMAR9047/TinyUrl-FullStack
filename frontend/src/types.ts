export interface ShortUrl {
  code: string;
  shortURL: string;
  originalURL: string;
  totalClicks: number;
  isPrivate: boolean;
  createdAt: string;
}

export interface ShortenUrlRequest {
  originalURL: string;
  isPrivate: boolean;
}
