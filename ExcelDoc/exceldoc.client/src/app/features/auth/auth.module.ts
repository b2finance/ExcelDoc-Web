import { NgModule } from '@angular/core';
import { SharedModule } from '../../shared/shared.module';
import { AuthRoutingModule } from './auth-routing.module';
import { LoginComponent } from './components/login/login.component';
import { SetupProgressComponent } from './components/setup-progress/setup-progress.component';

@NgModule({
  declarations: [LoginComponent, SetupProgressComponent],
  imports: [SharedModule, AuthRoutingModule]
})
export class AuthModule {}
