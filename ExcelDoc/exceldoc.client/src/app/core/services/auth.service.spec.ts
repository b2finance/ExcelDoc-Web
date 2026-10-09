import { HttpClientTestingModule } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AUTH_ROLES, AuthService, LoginResponse } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(AuthService);
  });

  for (const nomeUsuario of ['manager', 'Support']) {
    it(`direciona ${nomeUsuario} para o mapeamento`, () => {
      expect(service.getDefaultRoute(session(nomeUsuario, AUTH_ROLES.administrator)))
        .toBe('/mapeamento');
    });
  }

  it('direciona um usuário comum para o mapeamento', () => {
    expect(service.getDefaultRoute(session('usuario.sap', AUTH_ROLES.user)))
      .toBe('/mapeamento');
  });

  it('direciona uma sessão ausente para o login', () => {
    expect(service.getDefaultRoute(null)).toBe('/login');
  });
});

function session(nomeUsuario: string, tipoUsuario: string): LoginResponse {
  return {
    token: 'token',
    expiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
    nomeUsuario,
    tipoUsuario,
    database: 'base'
  };
}
