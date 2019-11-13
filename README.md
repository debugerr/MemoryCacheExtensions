# MemoryCacheExtensions - Samples
This contains some sample extensions for the MemoryCache type:

1. GetOrCreateAtomicAsync
Is an example of how to Get or create an item in the memory cache atomically. Meaning, how to avoid multiple creations to happen, when you have multiple requests coming into the cache for the same item and you experience a cache miss.
