// LocalTranscriber Service Worker
// Caches model files for offline use and better memory management

const CACHE_NAME = 'localtranscriber-v1';
const MODEL_CACHE_NAME = 'localtranscriber-models-v1';

// App shell files to cache
const APP_SHELL = [
  '/LocalTranscriber/',
  '/LocalTranscriber/index.html',
  '/LocalTranscriber/css/app.css',
  '/LocalTranscriber/js/browserTranscriber.js',
  '/LocalTranscriber/js/browserRecorder.js',
];

// Install event - cache app shell
self.addEventListener('install', (event) => {
  console.log('[SW] Installing...');
  event.waitUntil(
    caches.open(CACHE_NAME).then((cache) => {
      console.log('[SW] Caching app shell');
      return cache.addAll(APP_SHELL).catch((err) => {
        console.warn('[SW] Some app shell files failed to cache:', err);
      });
    })
  );
  self.skipWaiting();
});

// Activate event - clean old caches
self.addEventListener('activate', (event) => {
  console.log('[SW] Activating...');
  event.waitUntil(
    caches.keys().then((keys) => {
      return Promise.all(
        keys
          .filter((key) => key !== CACHE_NAME && key !== MODEL_CACHE_NAME)
          .map((key) => {
            console.log('[SW] Deleting old cache:', key);
            return caches.delete(key);
          })
      );
    })
  );
  self.clients.claim();
});

// Fetch event - serve from cache, fetch from network
self.addEventListener('fetch', (event) => {
  const url = new URL(event.request.url);
  
  // Handle model files specially - cache them in model cache
  if (isModelFile(url)) {
    event.respondWith(handleModelFetch(event.request));
    return;
  }
  
  // For other requests, try cache first, then network
  event.respondWith(
    caches.match(event.request).then((cached) => {
      if (cached) {
        return cached;
      }
      return fetch(event.request).then((response) => {
        // Don't cache non-ok responses or non-GET requests
        if (!response.ok || event.request.method !== 'GET') {
          return response;
        }
        // Cache successful responses
        const responseClone = response.clone();
        caches.open(CACHE_NAME).then((cache) => {
          cache.put(event.request, responseClone);
        });
        return response;
      });
    })
  );
});

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

async function handleModelFetch(request) {
  const cache = await caches.open(MODEL_CACHE_NAME);
  
  // Check cache first
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
      // Cache the model file
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
  if (event.data.type === 'CLEAR_MODEL_CACHE') {
    caches.delete(MODEL_CACHE_NAME).then(() => {
      console.log('[SW] Model cache cleared');
      event.ports[0]?.postMessage({ success: true });
    });
  }
  
  if (event.data.type === 'GET_CACHE_SIZE') {
    getCacheSize().then((size) => {
      event.ports[0]?.postMessage({ size });
    });
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
