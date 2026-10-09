import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { AuthGuard } from './core/guards/auth.guard';
import { AUTH_ROLES } from './features/auth/models/auth.models';

const routes: Routes = [
  { path: '', redirectTo: 'mapeamento', pathMatch: 'full' },
  {
    path: 'login',
    loadChildren: () => import('./features/auth/auth.module').then((module) => module.AuthModule)
  },
  { path: 'dashboard', redirectTo: 'mapeamento', pathMatch: 'full' },
  {
    path: 'documentos',
    loadChildren: () => import('./features/documentos/documentos.module').then((module) => module.DocumentosModule),
    canActivate: [AuthGuard],
    data: { roles: [AUTH_ROLES.administrator] }
  },
  {
    path: 'colecoes',
    loadChildren: () => import('./features/colecoes/colecoes.module').then((module) => module.ColecoesModule),
    canActivate: [AuthGuard],
    data: { roles: [AUTH_ROLES.administrator] }
  },
  {
    path: 'mapeamento',
    loadChildren: () => import('./features/mapeamento/mapeamento.module').then((module) => module.MapeamentoModule),
    canActivate: [AuthGuard],
    data: { roles: [AUTH_ROLES.administrator, AUTH_ROLES.user] }
  },
  {
    path: 'perfil-mapeamento',
    loadChildren: () => import('./features/perfil-mapeamento/perfil-mapeamento.module').then((module) => module.PerfilMapeamentoModule),
    canActivate: [AuthGuard],
    data: { roles: [AUTH_ROLES.administrator] }
  },
  {
    path: 'processamento',
    loadChildren: () => import('./features/processamento/processamento.module').then((module) => module.ProcessamentoModule),
    canActivate: [AuthGuard],
    data: { roles: [AUTH_ROLES.administrator, AUTH_ROLES.user] }
  },
  { path: '**', redirectTo: 'login' }
];

@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule]
})
export class AppRoutingModule { }
