import { Component, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService, LoginResponse } from './core/services/auth.service';
import { LanguageService } from './core/services/language.service';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrl: './app.component.css'
})
export class AppComponent implements OnInit {
  title = 'ExcelDoc';

  constructor(
    private readonly authService: AuthService,
    private readonly router: Router,
    private readonly languageService: LanguageService
  ) {}

  ngOnInit(): void {
    const session = this.authService.getSession();
    this.languageService.inicializar(session?.idioma ?? undefined);
  }

  get session(): LoginResponse | null {
    return this.authService.getSession();
  }

  get isAuthenticated(): boolean {
    return this.session !== null;
  }

  get databaseLabel(): string {
    return this.session?.database.trim() ?? '';
  }

  changePartner(): void {
    this.authService.changePartner().subscribe({
      next: () => {
        void this.router.navigate(['/login']);
      },
      error: () => {
        this.authService.logout();
        void this.router.navigate(['/login']);
      }
    });
  }

  logout(): void {
    this.authService.logout();
    void this.router.navigate(['/login']);
  }
}
