import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MapeamentoService } from './mapeamento.service';

describe('MapeamentoService example spreadsheet', () => {
  let service: MapeamentoService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(MapeamentoService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends the selected profile in the query and revalidates the download', () => {
    service.downloadPlanilhaExemplo(42).subscribe();

    const request = http.expectOne('/api/planilhas/exemplo?perfilId=42');
    expect(request.request.method).toBe('GET');
    expect(request.request.headers.get('Cache-Control')).toBe('no-cache');
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob());
  });

  it('uses a separate endpoint for the standard spreadsheet', () => {
    service.downloadPlanilhaExemploPadrao().subscribe();

    const request = http.expectOne('/api/planilhas/exemplo/padrao');
    expect(request.request.method).toBe('GET');
    request.flush(new Blob());
  });
});
