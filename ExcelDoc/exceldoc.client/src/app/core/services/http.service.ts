import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

type HttpQueryParamValue = string | number | boolean | ReadonlyArray<string | number | boolean>;

export interface HttpRequestOptions {
  headers?: HttpHeaders | Record<string, string | string[]>;
  params?: HttpParams | Record<string, HttpQueryParamValue>;
}

@Injectable({
  providedIn: 'root'
})
export class HttpService {
  constructor(private readonly httpClient: HttpClient) { }

  get<T>(url: string, options?: HttpRequestOptions): Observable<T> {
    return this.httpClient.get<T>(url, options);
  }

  getBlob(url: string, options?: HttpRequestOptions): Observable<Blob> {
    return this.httpClient.get(url, { ...options, responseType: 'blob' });
  }

  post<TResponse, TRequest>(url: string, body: TRequest): Observable<TResponse> {
    return this.httpClient.post<TResponse>(url, body);
  }

  put<TResponse, TRequest>(url: string, body: TRequest): Observable<TResponse> {
    return this.httpClient.put<TResponse>(url, body);
  }

  delete<T>(url: string): Observable<T> {
    return this.httpClient.delete<T>(url);
  }
}
