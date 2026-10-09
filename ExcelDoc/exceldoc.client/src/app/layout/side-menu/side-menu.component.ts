import { Component } from '@angular/core';
import { AUTH_ROLES, AuthService } from '../../core/services/auth.service';

interface MenuItem {
  labelKey: string;
  icon: string;
  route: string;
  roles: string[];
  exact?: boolean;
}

@Component({
  selector: 'app-side-menu',
  templateUrl: './side-menu.component.html',
  styleUrl: './side-menu.component.css'
})
export class SideMenuComponent {
  readonly menuItems: MenuItem[] = [
    {
      labelKey: 'layout.sideMenu.items.documents.label',
      icon: 'description',
      route: '/documentos',
      roles: [AUTH_ROLES.administrator],
      exact: true
    },
    {
      labelKey: 'layout.sideMenu.items.mappings.label',
      icon: 'account_tree',
      route: '/mapeamento',
      roles: [AUTH_ROLES.administrator, AUTH_ROLES.user]
    },
    {
      labelKey: 'layout.sideMenu.items.mappingProfile.label',
      icon: 'schema',
      route: '/perfil-mapeamento',
      roles: [AUTH_ROLES.administrator],
      exact: true
    },
    {
      labelKey: 'layout.sideMenu.items.collections.label',
      icon: 'folder_copy',
      route: '/colecoes',
      roles: [AUTH_ROLES.administrator],
      exact: true
    },
    {
      labelKey: 'layout.sideMenu.items.processing.label',
      icon: 'settings_slow_motion',
      route: '/processamento',
      roles: [AUTH_ROLES.administrator, AUTH_ROLES.user]
    }
  ];

  constructor(private readonly authService: AuthService) { }

  get isAuthenticated(): boolean {
    return this.authService.getSession() !== null;
  }

  get availableMenuItems(): MenuItem[] {
    return this.menuItems.filter((item) => this.authService.hasRequiredRoles(item.roles));
  }
}
