// LocalTranscriber Service Worker
// Caches model files for offline use and better memory management
// App files use cache-busting strategy

const APP_VERSION = '{{VERSION}}'; // Replaced at build time
const CACHE_NAME = `localtranscriber-app-${APP_VERSION}`;
const MODEL_CACHE_NAME = 'localtranscriber-models-v1';

// App shell files - cached with version
const APP_SHELL = [
  './',
  './index.html',
  './css/app.css',
  './manifest.json',
];

// Install event - cache app shell
self.addEventListener('install', (event) => {
  console.log(`[SW] Installing v${APP_VERSION}...`);
  event.waitUntil(
    caches.open(CACHE_NAME).then((cache) => {
      console.log('[SW] Caching app shell');
      return cache.addAll(APP_SHELL).catch((err) => {
        console.warn('[SW] Some app shell files failed to cache:', err);
      });
    })
  );
  // Activate immediately, don't wait for old SW to release
  self.skipWaiting();
});

// Activate event - clean old app caches (keep model cache)
self.addEventListener('activate', (event) => {
  console.log(`[SW] Activating v${APP_VERSION}...`);
  event.waitUntil(
    caches.keys().then((keys) => {
      return Promise.all(
        keys
          .filter((key) => {
            // Keep model cache, delete old app caches
            if (key === MODEL_CACHE_NAME) return false;
            if (key === CACHE_NAME) return false;
            return key.startsWith('localtranscriber-');
          })
          .map((key) => {
            console.log('[SW] Deleting old cache:', key);
            return caches.delete(key);
          })
      );
    })
  );
  // Take control of all clients immediately
  self.clients.claim();
});

// Fetch event
self.addEventListener('fetch', (event) => {
  const url = new URL(event.request.url);
  
  // Handle model files specially - cache indefinitely
  if (isModelFile(url)) {
    event.respondWith(handleModelFetch(event.request));
    return;
  }
  
  // For Blazor framework files and app assets, use stale-while-revalidate
  if (isAppAsset(url)) {
    event.respondWith(staleWhileRevalidate(event.request));
    return;
  }
  
  // For navigation/HTML, always try network first for freshness
  if (event.request.mode === 'navigate') {
    event.respondWith(networkFirst(event.request));
    return;
  }
  
  // Default: network with cache fallback
  event.respondWith(networkFirst(event.request));
});

function isAppAsset(url) {
  return url.pathname.includes('/_framework/') ||
         url.pathname.endsWith('.css') ||
         url.pathname.endsWith('.js') ||
         url.pathname.endsWith('.wasm') ||
         url.pathname.endsWith('.dll');
}

function isModelFile(url) {
  const modelPatterns = [
    /huggingface\.co.*whisper/i,
    /cdn\.jsdelivr\.net.*whisper/i,
    /hf-mirror\.com.*whisper/i,
    /\.onnx$/i,
    /tokenizer.*\.json$/i,
    /config\.json$/i,
  ];
  return modelPatterns.some((pattern) => pattern.test(url.href));
}

// Network first, fall back to cache
async function networkFirst(request) {
  try {
    const response = await fetch(request);
    if (response.ok) {
      const cache = await caches.open(CACHE_NAME);
      cache.put(request, response.clone());
    }
    return response;
  } catch {
    const cached = await caches.match(request);
    if (cached) return cached;
    throw new Error('Network failed and no cache available');
  }
}

// Serve from cache immediately, update cache in background
async function staleWhileRevalidate(request) {
  const cache = await caches.open(CACHE_NAME);
  const cached = await cache.match(request);
  
  const fetchPromise = fetch(request).then((response) => {
    if (response.ok) {
      cache.put(request, response.clone());
    }
    return response;
  }).catch(() => cached);
  
  return cached || fetchPromise;
}

async function handleModelFetch(request) {
  const cache = await caches.open(MODEL_CACHE_NAME);
  
  // Check cache first for models
  const cached = await cache.match(request);
  if (cached) {
    console.log('[SW] Serving model from cache:', request.url.substring(0, 80));
    return cached;
  }
  
  // Fetch from network
  console.log('[SW] Fetching model:', request.url.substring(0, 80));
  try {
    const response = await fetch(request);
    if (response.ok) {
      const responseClone = response.clone();
      cache.put(request, responseClone).catch((err) => {
        console.warn('[SW] Failed to cache model (storage full?):', err);
      });
    }
    return response;
  } catch (err) {
    console.error('[SW] Model fetch failed:', err);
    throw err;
  }
}

// Listen for messages from the main thread
self.addEventListener('message', (event) => {
  if (event.data.type === 'SKIP_WAITING') {
    self.skipWaiting();
  }
  
  if (event.data.type === 'CLEAR_MODEL_CACHE') {
    caches.delete(MODEL_CACHE_NAME).then(() => {
      console.log('[SW] Model cache cleared');
      event.ports[0]?.postMessage({ success: true });
    });
  }
  
  if (event.data.type === 'CLEAR_APP_CACHE') {
    caches.delete(CACHE_NAME).then(() => {
      console.log('[SW] App cache cleared');
      event.ports[0]?.postMessage({ success: true });
    });
  }
  
  if (event.data.type === 'GET_CACHE_SIZE') {
    getCacheSize().then((size) => {
      event.ports[0]?.postMessage({ size });
    });
  }
  
  if (event.data.type === 'GET_VERSION') {
    event.ports[0]?.postMessage({ version: APP_VERSION });
  }
});

async function getCacheSize() {
  try {
    const cache = await caches.open(MODEL_CACHE_NAME);
    const keys = await cache.keys();
    let totalSize = 0;
    
    for (const request of keys) {
      const response = await cache.match(request);
      if (response) {
        const blob = await response.clone().blob();
        totalSize += blob.size;
      }
    }
    
    return totalSize;
  } catch {
    return 0;
  }
}
