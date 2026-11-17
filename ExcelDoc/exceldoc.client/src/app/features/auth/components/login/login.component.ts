import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService, DevHubPartner, DevHubSelection } from '../../../../core/services/auth.service';

@Component({
  selector: 'app-login',
  templateUrl: './login.component.html',
  styleUrl: './login.component.css'
})
export class LoginComponent implements OnInit {
  partners: DevHubPartner[] = [];
  selection: DevHubSelection | null = null;
  partnerId: number | null = null;
  databaseId: number | null = null;
  errorMessage = '';
  userName = '';
  devHubPassword = '';
  sapPassword = '';
  busy = false;
  rememberUser = false;
  showDevHubPassword = false;
  showSapPassword = false;

  constructor(private readonly authService: AuthService, private readonly router: Router) {}

  ngOnInit(): void {
    try {
      this.userName = localStorage.getItem('exceldoc.rememberedUser') ?? '';
      this.rememberUser = !!this.userName;
    } catch {
      // O acesso continua disponível quando o armazenamento estiver bloqueado.
    }

    const session = this.authService.getSession();
    if (session) {
      void this.router.navigate([this.authService.getDefaultRoute(session)]);
      return;
    }

    this.partners = this.authService.getPartners();
  }

  authenticate(): void {
    this.errorMessage = '';
    if (!this.userName.trim() || !this.devHubPassword) {
      this.errorMessage = 'Informe seu usuário e sua senha DevHub.';
      return;
    }

    this.busy = true;
    this.authService.authenticate(this.userName.trim(), this.devHubPassword)
      .pipe(finalize(() => {
        this.busy = false;
        this.devHubPassword = '';
      }))
      .subscribe({
        next: response => {
          try {
            if (this.rememberUser) {
              localStorage.setItem('exceldoc.rememberedUser', this.userName.trim());
            } else {
              localStorage.removeItem('exceldoc.rememberedUser');
            }
          } catch {
            // Lembrar o usuário é opcional e não deve impedir o acesso.
          }
          this.partners = response.partners;
          if (!this.partners.length) {
            this.errorMessage = 'Nenhum cliente ativo está disponível para este usuário.';
          }
        },
        error: (error: HttpErrorResponse) => {
          this.errorMessage = error.error?.detail ?? 'Não foi possível autenticar no DevHub.';
        }
      });
  }

  selectPartner(): void {
    this.selection = null;
    this.databaseId = null;
    this.sapPassword = '';
    this.errorMessage = '';
    if (this.partnerId === null) {
      return;
    }

    this.busy = true;
    this.authService.selectPartner(this.partnerId)
      .pipe(finalize(() => { this.busy = false; }))
      .subscribe({
        next: selection => {
          this.selection = selection;
          if (selection.databases.length === 1) {
            this.databaseId = selection.databases[0].id;
          }
          if (!selection.databases.length) {
            this.errorMessage = 'Nenhuma base SAP está disponível para este cliente.';
          }
        },
        error: (error: HttpErrorResponse) => {
          this.errorMessage = error.error?.detail ?? 'Não foi possível carregar os dados do cliente.';
          if (error.status === 401) {
            this.authService.logout();
            this.partners = [];
          }
        }
      });
  }

  connect(): void {
    if (this.partnerId === null || this.databaseId === null || !this.sapPassword) {
      this.errorMessage = 'Selecione uma base e informe a senha SAP.';
      return;
    }

    this.authService.setPendingLogin({
      partnerId: this.partnerId,
      databaseId: this.databaseId,
      password: this.sapPassword
    });
    this.sapPassword = '';
    void this.router.navigate(['/login/preparando']);
  }

  logout(): void {
    this.authService.logout();
    this.partners = [];
    this.selection = null;
    this.partnerId = null;
    this.sapPassword = '';
    this.errorMessage = '';
  }
}
