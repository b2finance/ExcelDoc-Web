import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { EMPTY, Observable, catchError, tap } from 'rxjs';
import {
  AUTH_ROLES,
  DevHubLoginResponse,
  DevHubPartner,
  DevHubSelection,
  LoginRequest,
  LoginResponse
} from '../../features/auth/models/auth.models';

export {
  AUTH_ROLES,
  type DevHubPartner,
  type DevHubSelection,
  type LoginRequest,
  type LoginResponse
} from '../../features/auth/models/auth.models';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly apiUrl = '/api/devhub-auth';
  private readonly tokenStorageKey = 'exceldoc.jwt';
  private readonly sessionStorageKey = 'exceldoc.session';
  private readonly devHubKey = 'exceldoc.devhub';
  private readonly partnersKey = 'exceldoc.partners';
  private pendingLogin: LoginRequest | null = null;

  constructor(private readonly http: HttpClient) {}

  authenticate(userName: string, password: string): Observable<DevHubLoginResponse> {
    return this.http.post<DevHubLoginResponse>(`${this.apiUrl}/login`, { userName, password }).pipe(
      tap(response => {
        sessionStorage.setItem(this.devHubKey, response.sessionKey);
        sessionStorage.setItem(this.partnersKey, JSON.stringify(response.partners));
      })
    );
  }

  getPartners(): DevHubPartner[] {
    if (!sessionStorage.getItem(this.devHubKey)) {
      return [];
    }

    try {
      return JSON.parse(sessionStorage.getItem(this.partnersKey) ?? '[]') as DevHubPartner[];
    } catch {
      return [];
    }
  }

  selectPartner(partnerId: number): Observable<DevHubSelection> {
    return this.http.post<DevHubSelection>(`${this.apiUrl}/select`, { partnerId }, this.devHubOptions());
  }

  login(request: LoginRequest): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.apiUrl}/connect`, request, this.devHubOptions()).pipe(
      tap(response => {
        sessionStorage.setItem(this.tokenStorageKey, response.token);
        sessionStorage.setItem(this.sessionStorageKey, JSON.stringify(response));
      })
    );
  }

  setPendingLogin(request: LoginRequest): void {
    this.pendingLogin = { ...request };
  }

  takePendingLogin(): LoginRequest | null {
    const request = this.pendingLogin;
    this.pendingLogin = null;
    return request;
  }

  changePartner(): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/disconnect`, {}, this.devHubOptions()).pipe(
      tap(() => this.clearLocalSession())
    );
  }

  logout(): void {
    const options = this.devHubOptions();
    this.clearLocalSession();
    sessionStorage.removeItem(this.devHubKey);
    sessionStorage.removeItem(this.partnersKey);
    this.pendingLogin = null;
    this.http.post<void>(`${this.apiUrl}/logout`, {}, options)
      .pipe(catchError(() => EMPTY))
      .subscribe();
  }

  private devHubOptions(): { headers: { 'X-DevHub-Session': string } } {
    return { headers: { 'X-DevHub-Session': sessionStorage.getItem(this.devHubKey) ?? '' } };
  }

  getToken(): string | null {
    return sessionStorage.getItem(this.tokenStorageKey);
  }

  getSession(): LoginResponse | null {
    const rawValue = sessionStorage.getItem(this.sessionStorageKey);
    if (!rawValue) {
      return null;
    }

    try {
      const session = JSON.parse(rawValue) as LoginResponse;
      const expiresAt = Date.parse(session.expiresAtUtc);
      if (!Number.isFinite(expiresAt) || expiresAt <= Date.now()) {
        this.clearLocalSession();
        return null;
      }

      return session;
    } catch {
      this.clearLocalSession();
      return null;
    }
  }

  private clearLocalSession(): void {
    sessionStorage.removeItem(this.tokenStorageKey);
    sessionStorage.removeItem(this.sessionStorageKey);
  }

  isAdministrator(session: LoginResponse | null = this.getSession()): boolean {
    return session?.tipoUsuario === AUTH_ROLES.administrator;
  }

  hasRequiredRoles(requiredRoles: readonly string[] = [], session: LoginResponse | null = this.getSession()): boolean {
    if (!session) {
      return false;
    }

    if (!requiredRoles.length) {
      return true;
    }

    return requiredRoles.includes(session.tipoUsuario);
  }

  getDefaultRoute(session: LoginResponse | null = this.getSession()): string {
    if (this.hasRequiredRoles([AUTH_ROLES.administrator, AUTH_ROLES.user], session)) {
      return '/mapeamento';
    }

    return '/login';
  }
}
