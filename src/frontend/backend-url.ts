export function backendUrl(value = 'http://127.0.0.1:5080'): string {
  const invalid = () => new Error('BACKEND_URL must be an HTTP loopback origin without credentials, path, query or fragment.');
  let url: URL;
  try { url = new URL(value); } catch { throw invalid(); }
  if (url.protocol !== 'http:' || !['127.0.0.1', 'localhost', '[::1]'].includes(url.hostname)
    || url.username || url.password || url.pathname !== '/' || url.search || url.hash || value.includes('?') || value.includes('#')) throw invalid();
  return url.origin;
}
