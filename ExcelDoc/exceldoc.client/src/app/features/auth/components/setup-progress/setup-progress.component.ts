import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService, LoginResponse } from '../../../../core/services/auth.service';

interface SchemaPreviewRow {
  table: string;
  field: string;
  description: string;
}

@Component({
  selector: 'app-setup-progress',
  templateUrl: './setup-progress.component.html',
  styleUrl: './setup-progress.component.css'
})
export class SetupProgressComponent implements OnInit {
  errorMessage = '';
  isPreparing = true;

  constructor(
    private readonly authService: AuthService,
    private readonly router: Router
  ) {}

  readonly schemaRows: SchemaPreviewRow[] = [
    { table: 'EXD_DOCUMENTO', field: 'NomeDocumento', description: 'Cadastro de documentos SAP' },
    { table: 'EXD_COLECAO', field: 'NomeColecao', description: 'Coleções de campos' },
    { table: 'EXD_MAPEAMENTO', field: 'Nome', description: 'Mapeamentos padrão' },
    { table: 'EXD_MAPCAMPO', field: 'NomeCampo', description: 'Campos dos mapeamentos' },
    { table: 'EXD_PERFILMAP', field: 'DocumentoId', description: 'Perfis de documentos' }
  ];

  ngOnInit(): void {
    const request = this.authService.takePendingLogin();

    if (!request) {
      void this.router.navigate(['/login']);
      return;
    }

    this.authService.login(request).subscribe({
      next: (response: LoginResponse) => {
        void this.router.navigate([this.authService.getDefaultRoute(response)]);
      },
      error: (error: HttpErrorResponse) => {
        this.errorMessage = error.error?.detail ?? 'Não foi possível realizar o login.';
        this.isPreparing = false;
      }
    });
  }

  returnToLogin(): void {
    void this.router.navigate(['/login']);
  }
}
