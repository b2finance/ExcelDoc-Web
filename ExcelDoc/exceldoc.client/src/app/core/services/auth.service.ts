import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { EMPTY, Observable, catchError, tap } from 'rxjs';
import {
  AUTH_ROLES,
  LoginRequest,
  LoginResponse,
  SapBase
} from '../../features/auth/models/auth.models';

export {
  AUTH_ROLES,
  type LoginRequest,
  type LoginResponse,
  type SapBase
} from '../../features/auth/models/auth.models';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly apiUrl = '/api/auth';
  private readonly tokenStorageKey = 'exceldoc.jwt';
  private readonly sessionStorageKey = 'exceldoc.session';

  constructor(private readonly http: HttpClient) {}

  login(request: LoginRequest): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.apiUrl}/login`, request).pipe(
      tap((response: LoginResponse) => {
        localStorage.setItem(this.tokenStorageKey, response.token);
        localStorage.setItem(this.sessionStorageKey, JSON.stringify(response));
      })
    );
  }

  getBases(): Observable<SapBase[]> {
    return this.http.get<SapBase[]>(`${this.apiUrl}/bases`);
  }

  logout(): void {
    const token = this.getToken();

    this.clearLocalSession();

    const options = token
      ? { headers: { Authorization: `Bearer ${token}` } }
      : {};

    this.http.post<void>(`${this.apiUrl}/logout`, {}, options)
      .pipe(catchError(() => EMPTY))
      .subscribe();
  }

  getToken(): string | null {
    return localStorage.getItem(this.tokenStorageKey);
  }

  getSession(): LoginResponse | null {
    const rawValue = localStorage.getItem(this.sessionStorageKey);

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
    localStorage.removeItem(this.tokenStorageKey);
    localStorage.removeItem(this.sessionStorageKey);
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
