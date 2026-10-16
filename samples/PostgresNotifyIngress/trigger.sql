-- Announces every change to the documents table of Hasseware.AspNetCore.Components.Bpmn.Storage.EfCore on the channel "bpmn_documents".
-- The payload is small (well under the 8000 bytes NOTIFY allows) and never carries content.
CREATE OR REPLACE FUNCTION bpmn_documents_notify() RETURNS trigger AS $$
DECLARE
  changed record;
BEGIN
  IF TG_OP = 'DELETE' THEN changed := OLD; ELSE changed := NEW; END IF;
  PERFORM pg_notify('bpmn_documents', json_build_object(
    'tenant', changed."Tenant",
    'path', changed."Path",
    'version', changed."Version",
    'op', TG_OP)::text);
  RETURN NULL;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER bpmn_documents_changed
  AFTER INSERT OR UPDATE OR DELETE ON "BpmnDocuments"
  FOR EACH ROW EXECUTE FUNCTION bpmn_documents_notify();
