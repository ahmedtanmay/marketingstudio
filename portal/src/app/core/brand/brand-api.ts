import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  Brand,
  BrandProfileInput,
  BrandSummary,
  BrandVoice,
  Faq,
  KnowledgeDocument,
  KnowledgeSearchResult,
  KnowledgeUsage,
  MediaAsset,
  OnboardingStatus,
  PagedResult,
  Product,
  ProductInput,
} from '../api/api.types';

const enc = encodeURIComponent;

/** BRD / PRD / TEN-1 endpoints. */
@Injectable({ providedIn: 'root' })
export class BrandApi {
  private readonly http = inject(HttpClient);

  listBrands(): Observable<BrandSummary[]> {
    return this.http.get<BrandSummary[]>('/api/v1/brands');
  }

  getBrand(brandId: string): Observable<Brand> {
    return this.http.get<Brand>(`/api/v1/brands/${enc(brandId)}`);
  }

  createBrand(profile: BrandProfileInput): Observable<Brand> {
    return this.http.post<Brand>('/api/v1/brands', profile);
  }

  updateProfile(brandId: string, profile: BrandProfileInput): Observable<Brand> {
    return this.http.put<Brand>(`/api/v1/brands/${enc(brandId)}`, profile);
  }

  updateVoice(brandId: string, voice: BrandVoice): Observable<Brand> {
    return this.http.put<Brand>(`/api/v1/brands/${enc(brandId)}/voice`, voice);
  }

  deleteBrand(brandId: string, confirmName: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/brands/${enc(brandId)}`, { params: { confirmName } });
  }

  searchProducts(brandId: string, query: { search?: string; category?: string; page: number; pageSize: number }): Observable<PagedResult<Product>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.search) {
      params = params.set('search', query.search);
    }
    if (query.category) {
      params = params.set('category', query.category);
    }
    return this.http.get<PagedResult<Product>>(`/api/v1/brands/${enc(brandId)}/products`, { params });
  }

  productCategories(brandId: string): Observable<string[]> {
    return this.http.get<string[]>(`/api/v1/brands/${enc(brandId)}/products/categories`);
  }

  createProduct(brandId: string, product: ProductInput): Observable<Product> {
    return this.http.post<Product>(`/api/v1/brands/${enc(brandId)}/products`, product);
  }

  updateProduct(brandId: string, productId: string, product: ProductInput): Observable<Product> {
    return this.http.put<Product>(`/api/v1/brands/${enc(brandId)}/products/${enc(productId)}`, product);
  }

  deleteProduct(brandId: string, productId: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/brands/${enc(brandId)}/products/${enc(productId)}`);
  }

  listMedia(brandId: string, tag?: string): Observable<MediaAsset[]> {
    return this.http.get<MediaAsset[]>(`/api/v1/brands/${enc(brandId)}/media`, { params: tag ? { tag } : {} });
  }

  uploadMedia(brandId: string, file: File, tags: string[]): Observable<MediaAsset> {
    const form = new FormData();
    form.append('file', file, file.name);
    if (tags.length > 0) {
      form.append('tags', tags.join(','));
    }
    return this.http.post<MediaAsset>(`/api/v1/brands/${enc(brandId)}/media`, form);
  }

  updateMediaTags(brandId: string, assetId: string, tags: string[]): Observable<MediaAsset> {
    return this.http.put<MediaAsset>(`/api/v1/brands/${enc(brandId)}/media/${enc(assetId)}/tags`, { tags });
  }

  deleteMedia(brandId: string, assetId: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/brands/${enc(brandId)}/media/${enc(assetId)}`);
  }

  // ---- knowledge base (KB-1..4) ----

  listDocuments(brandId: string): Observable<KnowledgeDocument[]> {
    return this.http.get<KnowledgeDocument[]>(`/api/v1/brands/${enc(brandId)}/knowledge/documents`);
  }

  uploadDocument(brandId: string, file: File): Observable<KnowledgeDocument> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<KnowledgeDocument>(`/api/v1/brands/${enc(brandId)}/knowledge/documents`, form);
  }

  retryDocument(brandId: string, documentId: string): Observable<KnowledgeDocument> {
    return this.http.post<KnowledgeDocument>(`/api/v1/brands/${enc(brandId)}/knowledge/documents/${enc(documentId)}/retry`, {});
  }

  deleteDocument(brandId: string, documentId: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/brands/${enc(brandId)}/knowledge/documents/${enc(documentId)}`);
  }

  knowledgeUsage(brandId: string): Observable<KnowledgeUsage> {
    return this.http.get<KnowledgeUsage>(`/api/v1/brands/${enc(brandId)}/knowledge/usage`);
  }

  listFaqs(brandId: string): Observable<Faq[]> {
    return this.http.get<Faq[]>(`/api/v1/brands/${enc(brandId)}/knowledge/faqs`);
  }

  createFaq(brandId: string, faq: { question: string; answer: string }): Observable<Faq> {
    return this.http.post<Faq>(`/api/v1/brands/${enc(brandId)}/knowledge/faqs`, faq);
  }

  updateFaq(brandId: string, faqId: string, faq: { question: string; answer: string }): Observable<Faq> {
    return this.http.put<Faq>(`/api/v1/brands/${enc(brandId)}/knowledge/faqs/${enc(faqId)}`, faq);
  }

  deleteFaq(brandId: string, faqId: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/brands/${enc(brandId)}/knowledge/faqs/${enc(faqId)}`);
  }

  searchKnowledge(brandId: string, query: string): Observable<KnowledgeSearchResult> {
    return this.http.post<KnowledgeSearchResult>(`/api/v1/brands/${enc(brandId)}/knowledge/search`, { query, topK: 5 });
  }

  onboarding(): Observable<OnboardingStatus> {
    return this.http.get<OnboardingStatus>('/api/v1/onboarding');
  }
}
