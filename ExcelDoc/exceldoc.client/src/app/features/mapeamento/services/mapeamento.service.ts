import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { HttpService } from '../../../core/services/http.service';
import {
  Mapeamento,
  AtualizarMapeamentoCamposPayload,
  MapeamentoCampo,
  MapeamentoCampoPayload,
  MapeamentoPayload
} from '../models/mapeamento.model';

@Injectable({
  providedIn: 'root'
})
export class MapeamentoService {
  private readonly mapeamentosApiUrl = '/api/mapeamentos';
  private readonly mapeamentosCamposApiUrl = '/api/mapeamentos-campos';

  constructor(private readonly httpService: HttpService) { }

  getMapeamentosByColecao(colecaoId: number): Observable<Mapeamento[]> {
    return this.httpService.get<Mapeamento[]>(`${this.mapeamentosApiUrl}/colecao/${colecaoId}`);
  }

  createMapeamento(mapeamento: MapeamentoPayload): Observable<Mapeamento> {
    return this.httpService.post<Mapeamento, MapeamentoPayload>(this.mapeamentosApiUrl, mapeamento);
  }

  cloneMapeamento(id: number, nome: string): Observable<Mapeamento> {
    return this.httpService.post<Mapeamento, { nome: string }>(`${this.mapeamentosApiUrl}/${id}/clone`, { nome });
  }

  updateMapeamento(id: number, mapeamento: MapeamentoPayload): Observable<Mapeamento> {
    return this.httpService.put<Mapeamento, MapeamentoPayload>(`${this.mapeamentosApiUrl}/${id}`, mapeamento);
  }

  deleteMapeamento(id: number): Observable<void> {
    return this.httpService.delete<void>(`${this.mapeamentosApiUrl}/${id}`);
  }

  getCamposByMapeamento(mapeamentoId: number): Observable<MapeamentoCampo[]> {
    return this.httpService.get<MapeamentoCampo[]>(`${this.mapeamentosCamposApiUrl}/${mapeamentoId}`);
  }

  createCampo(mapeamento: MapeamentoCampoPayload): Observable<MapeamentoCampo> {
    return this.httpService.post<MapeamentoCampo, MapeamentoCampoPayload>(this.mapeamentosCamposApiUrl, mapeamento);
  }

  updateCampo(id: number, mapeamento: MapeamentoCampoPayload): Observable<MapeamentoCampo> {
    return this.httpService.put<MapeamentoCampo, MapeamentoCampoPayload>(`${this.mapeamentosCamposApiUrl}/${id}`, mapeamento);
  }

  deleteCampo(id: number): Observable<void> {
    return this.httpService.delete<void>(`${this.mapeamentosCamposApiUrl}/${id}`);
  }

  replaceCampos(mapeamentoId: number, payload: AtualizarMapeamentoCamposPayload): Observable<MapeamentoCampo[]> {
    return this.httpService.put<MapeamentoCampo[], AtualizarMapeamentoCamposPayload>(
      `${this.mapeamentosCamposApiUrl}/mapeamento/${mapeamentoId}`,
      payload
    );
  }

  previewExcel(arquivo: File): Observable<{ colunas: string[] }> {
    const formData = new FormData();
    formData.append('arquivo', arquivo);

    return this.httpService.post<{ colunas: string[] }, FormData>(
      `${this.mapeamentosApiUrl}/preview-excel`,
      formData
    );
  }

  downloadPlanilhaExemplo(perfilId: number | null): Observable<Blob> {
    const params = perfilId === null ? undefined : { perfilId };
    return this.httpService.getBlob('/api/planilhas/exemplo', {
      params,
      headers: { 'Cache-Control': 'no-cache' }
    });
  }

  downloadPlanilhaExemploPadrao(): Observable<Blob> {
    return this.httpService.getBlob('/api/planilhas/exemplo/padrao');
  }
}
