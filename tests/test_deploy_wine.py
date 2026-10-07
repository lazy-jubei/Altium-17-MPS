import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import xml.etree.ElementTree as ET
spec = importlib.util.spec_from_file_location('deploy_wine', Path(__file__).parents[1] / 'tools/deploy-wine.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class DeploymentTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.prefix = Path(self.temp.name)/'prefix'
        self.root = self.prefix/'drive_c/ProgramData/Altium/Altium Designer {1234}/Extensions'
        self.root.mkdir(parents=True)
        self.registry = self.root/'ExtensionsRegistry.xml'
        self.registry.write_text('<Extensions><Item HRID="Other"><Path>C:\\Other</Path></Item></Extensions>')
        self.dist = Path(self.temp.name)/'dist'; self.dist.mkdir()
        for name in module.REQUIRED: (self.dist/name).write_text('new build')
        (self.dist/'Altium17-PartSearch.target.json').write_text(json.dumps({'altiumVersion':'17','framework':'net48','architecture':'x86'}))
    def test_install_and_update_preserve_other_extension(self):
        target, backup = module.deploy(self.dist,self.prefix)
        self.assertTrue((backup/'ExtensionsRegistry.xml').exists())
        self.assertTrue((target/module.REQUIRED[0]).exists())
        self.assertTrue(ET.parse(self.registry).getroot().find("Item[@HRID='Other']") is not None)
        target.joinpath('stale.dll').write_text('old dependency')
        _, second_backup = module.deploy(self.dist,self.prefix)
        items=ET.parse(self.registry).getroot().findall("Item[@HRID='Altium17-PartSearch']")
        self.assertEqual(len(items),1)
        self.assertEqual(items[0].findtext('Version'),'0.4.1.0')
        self.assertTrue(items[0].findtext('Path').startswith('C:\\ProgramData'))
        self.assertFalse((target/'stale.dll').exists())
        self.assertTrue((second_backup/module.HRID/'stale.dll').exists())
    def test_dry_run_does_not_write(self):
        before=self.registry.read_bytes(); module.deploy(self.dist,self.prefix,dry_run=True)
        self.assertEqual(self.registry.read_bytes(),before)
        self.assertFalse((self.root/module.HRID).exists())
    def test_ambiguous_installations_are_rejected(self):
        second=self.prefix/'drive_c/ProgramData/Altium/Altium Designer {5678}/Extensions'
        second.mkdir(parents=True); (second/'ExtensionsRegistry.xml').write_text('<Extensions/>')
        with self.assertRaises(ValueError):module.deploy(self.dist,self.prefix)
        module.deploy(self.dist,self.prefix,self.root)
    def test_invalid_registry_is_rejected_before_copy(self):
        self.registry.write_text('<not-xml')
        with self.assertRaises(ET.ParseError):module.deploy(self.dist,self.prefix)
        self.assertFalse((self.root/module.HRID).exists())
    def test_registry_write_failure_restores_previous_plugin(self):
        target,_=module.deploy(self.dist,self.prefix)
        (target/'old-file.txt').write_text('keep me'); before=self.registry.read_bytes()
        with mock.patch.object(module.os,'replace',side_effect=OSError('simulated write failure')):
            with self.assertRaises(OSError):module.deploy(self.dist,self.prefix)
        self.assertEqual(self.registry.read_bytes(),before)
        self.assertEqual((target/'old-file.txt').read_text(),'keep me')
    def test_ad17_registration_repairs_incompatible_minimum_versions(self):
        module.deploy(self.dist,self.prefix)
        tree=ET.parse(self.registry)
        item=tree.getroot().find("Item[@HRID='Altium17-PartSearch']")
        item.find('PlatformVersions/DXP').set('BuildNumber','99.0.0.0')
        item.find('PlatformVersions/EDP').set('BuildNumber','99.0.0.0')
        tree.write(self.registry)
        (self.dist/'Altium17-PartSearch.target.json').write_text(json.dumps({'altiumVersion':'17','framework':'net48','architecture':'x86'}))
        module.deploy(self.dist,self.prefix,altium_version='17')
        item=ET.parse(self.registry).getroot().find("Item[@HRID='Altium17-PartSearch']")
        self.assertEqual(item.find('PlatformVersions/DXP').get('BuildNumber'),'1.0.5.5')
        self.assertEqual(item.find('PlatformVersions/EDP').get('BuildNumber'),'10.0.5.5')
        self.assertTrue(ET.parse(self.registry).getroot().find("Item[@HRID='Other']") is not None)

    def test_wrong_target_is_rejected_before_deployment(self):
        manifest=self.dist/'Altium17-PartSearch.target.json'
        manifest.write_text(json.dumps({'altiumVersion':'26','framework':'net8.0-windows','architecture':'x64'}))
        before=self.registry.read_bytes()
        with self.assertRaises(ValueError):module.deploy(self.dist,self.prefix)
        self.assertEqual(self.registry.read_bytes(),before)
        self.assertFalse((self.root/module.HRID).exists())

if __name__=='__main__':unittest.main()
