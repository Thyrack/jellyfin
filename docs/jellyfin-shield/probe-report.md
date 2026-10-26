# Rapport de sonde Jellyfin sur NVIDIA Shield — 2026-10-25

## Données brutes collectées

### g0-raw.txt

```text

### props
[af.fast_downmix]: [1]
[af.resampler.quality]: [7]
[ap.interface]: [wlan0]
[atvremote.bleaudio_connectgatt_delay_ms]: [2500]
[audio.dolby.gaming_app]: [false]
[audio.dolby.youtube_app]: [false]
[audio.hdmi.mlp_support]: [true]
[audio.hw.passthrough]: [false]
[build.version.extensions.r]: [0]
[cache_key.bluetooth.get_bond_state]: [-8293684452152179837]
[cache_key.bluetooth.get_profile_connection_state]: [-8293684452152179834]
[cache_key.bluetooth.get_state]: [-8293684452152179833]
[cache_key.bluetooth.is_offloaded_filtering_supported]: [-8293684452152179842]
[cache_key.display_info]: [7257622275147820246]
[cache_key.has_system_feature]: [7257622275147820240]
[cache_key.is_compat_change_enabled]: [7257622275147820267]
[cache_key.is_interactive]: [7257622275147820231]
[cache_key.is_power_save_mode]: [7257622275147820249]
[cache_key.is_user_unlocked]: [7257622275147820255]
[cache_key.location_enabled]: [7257622275147820250]
[cache_key.package_info]: [7257622275147820268]
[camera.disable_zsl_mode]: [1]
[dalvik.vm.appimageformat]: [lz4]
[dalvik.vm.dex2oat-Xms]: [64m]
[dalvik.vm.dex2oat-Xmx]: [512m]
[dalvik.vm.dex2oat-max-image-block-size]: [524288]
[dalvik.vm.dex2oat-minidebuginfo]: [true]
[dalvik.vm.dex2oat-resolve-startup-strings]: [true]
[dalvik.vm.dex2oat-updatable-bcp-packages-file]: [/system/etc/updatable-bcp-packages.txt]
[dalvik.vm.dexopt.secondary]: [true]
[dalvik.vm.heapgrowthlimit]: [192m]
[dalvik.vm.heapmaxfree]: [8m]
[dalvik.vm.heapminfree]: [512k]
[dalvik.vm.heapsize]: [512m]
[dalvik.vm.heapstartsize]: [16m]
[dalvik.vm.heaptargetutilization]: [0.75]
[dalvik.vm.image-dex2oat-Xms]: [64m]
[dalvik.vm.image-dex2oat-Xmx]: [64m]
[dalvik.vm.isa.arm.features]: [default]
[dalvik.vm.isa.arm.variant]: [cortex-a15]
[dalvik.vm.isa.arm64.features]: [default]
[dalvik.vm.isa.arm64.variant]: [generic]
[dalvik.vm.minidebuginfo]: [true]
[dalvik.vm.usejit]: [true]
[dalvik.vm.usejitprofiles]: [true]
[debug.atrace.tags.enableflags]: [0]
[debug.force_rtl]: [false]
[debug.sf.disable_backpressure]: [1]
[debug.sf.high_fps_late_app_phase_offset_ns]: [100000]
[debug.sf.high_fps_late_sf_phase_offset_ns]: [100000]
[debug.stagefright.c2-poolmask]: [0x80000]
[debug.stagefright.ccodec]: [0]
[debug.tracing.screen_brightness]: [0.39763778]
[dev.bootcomplete]: [1]
[dev.mnt.blk.data_mirror.data_ce.b6790f0b-9d50-4c81-ba35-16de7a9d9549]: [dm-0]
[dev.mnt.blk.data_mirror.data_de.b6790f0b-9d50-4c81-ba35-16de7a9d9549]: [dm-0]
[dev.mnt.blk.mnt.expand.b6790f0b-9d50-4c81-ba35-16de7a9d9549]: [dm-0]
[drm.service.enabled]: [true]
[hwservicemanager.ready]: [true]
[init.svc.adb_enable]: [stopped]
[init.svc.adbd]: [running]
[init.svc.apexd-bootstrap]: [stopped]
[init.svc.apexd-snapshotde]: [stopped]
[init.svc.apexd]: [stopped]
[init.svc.audioserver]: [running]
[init.svc.bluetooth-1-0]: [running]
[init.svc.bootanim]: [stopped]
[init.svc.boringssl_self_test32]: [stopped]
[init.svc.boringssl_self_test32_vendor]: [stopped]
[init.svc.boringssl_self_test64]: [stopped]
[init.svc.boringssl_self_test64_vendor]: [stopped]
[init.svc.boringssl_self_test_apex32]: [stopped]
[init.svc.boringssl_self_test_apex64]: [stopped]
[init.svc.bpfloader]: [stopped]
[init.svc.cac_log_dumper]: [stopped]
[init.svc.camera-hal-2.4]: [running]
[init.svc.cameraserver]: [running]
[init.svc.cpl_common_svc_hal_1-0]: [running]
[init.svc.cplsvc_hal_1-0]: [running]
[init.svc.credstore]: [running]
[init.svc.cyupdate]: [stopped]
[init.svc.derive_sdk]: [stopped]
[init.svc.drm]: [running]
[init.svc.dumpstate-1-0]: [running]
[init.svc.eks2_client]: [stopped]
[init.svc.factory_tests]: [stopped]
[init.svc.factoryservice-hal-1-0]: [running]
[init.svc.gatekeeperd]: [running]
[init.svc.get-hddstat]: [stopped]
[init.svc.gpu]: [running]
[init.svc.hal_hubcore_default]: [running]
[init.svc.health-hal-2-0]: [running]
[init.svc.heapprofd]: [stopped]
[init.svc.hidl_memory]: [running]
[init.svc.hwcomposer-2-0]: [running]
[init.svc.hwservicemanager]: [running]
[init.svc.idmap2d]: [stopped]
[init.svc.incidentd]: [running]
[init.svc.installd]: [running]
[init.svc.iorapd]: [running]
[init.svc.keystore]: [running]
[init.svc.lmkd]: [running]
[init.svc.logd-auditctl]: [stopped]
[init.svc.logd-reinit]: [stopped]
[init.svc.logd]: [running]
[init.svc.mdnsd]: [running]
[init.svc.media.swcodec]: [running]
[init.svc.media]: [running]
[init.svc.mediadrm]: [running]
[init.svc.mediaextractor]: [running]
[init.svc.mediametrics]: [running]
[init.svc.mediavndsvc-1-0]: [running]
[init.svc.netd]: [running]
[init.svc.nmbd]: [running]
[init.svc.nv-input-2-0]: [running]
[init.svc.nvidia.rpx]: [running]
[init.svc.nvphsd]: [running]
[init.svc.pbc2]: [running]
[init.svc.power-hal-1-0]: [running]
[init.svc.samba]: [running]
[init.svc.servicemanager]: [running]
[init.svc.setup_fs]: [stopped]
[init.svc.ss_status_non_treble]: [stopped]
[init.svc.statscollectionproxy-hal-1-0]: [running]
[init.svc.statsd]: [running]
[init.svc.storaged]: [running]
[init.svc.supplicant_log]: [running]
[init.svc.surfaceflinger]: [running]
[init.svc.system_suspend]: [running]
[init.svc.thermal-hal-1-0]: [running]
[init.svc.tlk_daemon_non_treble]: [running]
[init.svc.tombstoned]: [running]
[init.svc.traced]: [running]
[init.svc.traced_perf]: [stopped]
[init.svc.traced_probes]: [running]
[init.svc.ueventd]: [running]
[init.svc.usb-hal-1-1]: [running]
[init.svc.usbd]: [stopped]
[init.svc.vendor.audio-hal]: [running]
[init.svc.vendor.cas-hal-1-2]: [running]
[init.svc.vendor.cec-hal-1-0]: [running]
[init.svc.vendor.configstore-hal]: [running]
[init.svc.vendor.drm-clearkey-hal-1-3]: [running]
[init.svc.vendor.drm-hal-1-0]: [running]
[init.svc.vendor.drm-widevine-hal-1-3]: [running]
[init.svc.vendor.gatekeeper-1-0]: [running]
[init.svc.vendor.gralloc-2-0]: [running]
[init.svc.vendor.keymaster-3-0.tegra]: [running]
[init.svc.vendor.lkm_loader]: [stopped]
[init.svc.vendor.media.omx]: [running]
[init.svc.vendor.memtrack-hal-1-0]: [running]
[init.svc.vendor.mossigner-hal-service]: [running]
[init.svc.vendor.nvphsd_setup]: [stopped]
[init.svc.vendor.wifi_hal_legacy]: [running]
[init.svc.vendor.wifiloader]: [stopped]
[init.svc.vendor_flash_recovery]: [stopped]
[init.svc.vendor_nvidia_hardware_convertibleport]: [running]
[init.svc.vendor_nvidia_hardware_gensysfs]: [running]
[init.svc.vendor_nvidia_hardware_thermalservice]: [running]
[init.svc.vndservicemanager]: [running]
[init.svc.vold]: [running]
[init.svc.wificond]: [running]
[init.svc.wpa_supplicant]: [running]
[init.svc.zygote]: [running]
[init.svc.zygote_secondary]: [running]
[log.tag.APM_AudioPolicyManager]: [D]
[log.tag.TegraHDMICEC]: []
[log.tag.stats_log]: [I]
[media.stagefright.thumbnail.prefer_hw_codecs]: [true]
[net.bt.name]: [Android]
[net.qtaguid_enabled]: [1]
[net.tcp.default_init_rwnd]: [60]
[nvwfd.gamemode]: [1]
[nvwfd.max_macroblocks]: [8160]
[persist.adb.nonblocking_ffs]: [0]
[persist.bluetooth.btsnoopsize]: [0x7D00]
[persist.sys.NV_ECO.IF.CHARGING]: [false]
[persist.sys.alter_pairing]: [false]
[persist.sys.boot.reason.history]: [reboot,nvpowarmenurestart,1792971183
[persist.sys.boot.reason]: []
[persist.sys.bt_hid_disconnect]: [false]
[persist.sys.bt_hs_disconnect]: [false]
[persist.sys.commchip_country]: []
[persist.sys.commchip_device]: [0x4354]
[persist.sys.commchip_vendor]: [0x02d0]
[persist.sys.country]: []
[persist.sys.cpu_volt_cap.alpha]: [50]
[persist.sys.dalvik.vm.lib.2]: [libart.so]
[persist.sys.device_provisioned]: [1]
[persist.sys.didim.enable]: [1]
[persist.sys.disable_rescue]: [true]
[persist.sys.displayinset.top]: [0]
[persist.sys.fflag.override.settings_fuse]: [true]
[persist.sys.fuse]: [true]
[persist.sys.hdmi.addr.playback]: [4]
[persist.sys.hdmi.keep_awake]: [0]
[persist.sys.isolated_storage]: [true]
[persist.sys.language]: []
[persist.sys.lmk.reportkills]: [false]
[persist.sys.locale]: [fr-FR]
[persist.sys.localevar]: []
[persist.sys.media.avsync]: [true]
[persist.sys.phs.tbc.prethrottle]: [0]
[persist.sys.power.powerlevel]: [true]
[persist.sys.profiler_ms]: [0]
[persist.sys.timezone]: [Pacific/Tahiti]
[persist.sys.ts_headset_plug_count]: [false]
[persist.sys.usb.config]: [mtp]
[persist.sys.vold.toolc]: [-1]
[persist.sys.webview.vmsize]: [221840928]
[persist.tegra.0x523dc0]: []
[persist.tegra.0x523dc2]: [0x210a]
[persist.tegra.0xe0036b]: [1]
[persist.tegra.AF73C63E]: [0x80007ffd]
[persist.traced.enable]: [1]
[persist.vendor.nvda.adb.tcp.enable]: [1]
[persist.vendor.nvda.adb.tcp.port]: [5555]
[persist.vendor.pbc.inf.socthres]: [20]
[persist.vendor.sys.NV_DISPXRES]: [1920]
[persist.vendor.sys.NV_DISPYRES]: [1080]
[persist.vendor.sys.NV_ECO.IF.CHARGING]: [false]
[persist.vendor.sys.NV_ECO.STATE.ISECO]: [false]
[persist.vendor.sys.NV_FPSLIMIT]: [0]
[persist.vendor.sys.display.resolution]: [1920x1080]
[persist.vendor.sys.dpy0.prism]: [prism_sys_node_none]
[persist.vendor.sys.nvidia.bom]: [670-12571-0130-001]
[persist.vendor.sys.phs.cpufreq.boost]: [5]
[persist.vendor.sys.phs.frt.boost]: [5]
[persist.vendor.sys.phs.frt.min]: [20]
[persist.vendor.sys.phs.gpufreq.boost]: [5]
[persist.vendor.sys.phs.tbc.loadappfrt]: [0]
[persist.vendor.sys.sf.realtime]: [1]
[persist.vendor.sys.vold.downgrade_usb]: [1]
[persist.vendor.sys.vold.toolc]: [-1]
[persist.vendor.tegra.0x523dc5]: []
[persist.vendor.tegra.58027529]: []
[persist.vendor.tegra.a3456abe]: []
[persist.vendor.tegra.composite.policy]: [composite-yuv]
[persist.vendor.tegra.composite.range]: [Auto]
[persist.vendor.tegra.decompression]: [cde-client]
[persist.vendor.tegra.hdmi.colorimetry]: [1]
[persist.vendor.tegra.hdmi.range]: [Limited]
[persist.vendor.tegra.hdmi.resolution]: []
[persist.vendor.tegra.hdmi.underscan]: [0.0]
[persist.vendor.tegra.hdmi.yuv.444]: [1]
[persist.vendor.tegra.hdmi.yuv.enable]: [1]
[persist.vendor.tlk.provisioncastappdatacount]: [1]
[persist.vendor.tlk.provisionrequestcount]: [5]
[persist.vendor.tlk.provisionresponsecount]: [5]
[persist.vendor.tlk.provisionwritecount1]: [2]
[persist.vendor.tlk.provisionwritecount2]: [2]
[persist.vendor.ts_headset_plug_count]: [false]
[persist.wlan.ti.calibrated]: [0]
[pm.dexopt.ab-ota]: [speed-profile]
[pm.dexopt.bg-dexopt]: [speed-profile]
[pm.dexopt.boot]: [verify]
[pm.dexopt.first-boot]: [quicken]
[pm.dexopt.inactive]: [verify]
[pm.dexopt.install]: [speed-profile]
[pm.dexopt.shared]: [speed]
[ro.actionable_compatible_property.enabled]: [false]
[ro.adb.secure]: [1]
[ro.af.client_heap_size_kbyte]: [10240]
[ro.allow.mock.location]: [0]
[ro.apex.updatable]: [false]
[ro.baseband]: [unknown]
[ro.board.platform]: [tegra]
[ro.boot.bllock]: [1]
[ro.boot.bootloader]: [32.00.2019.50-t210-c5cc57a8]
[ro.boot.bootreason]: [warm]
[ro.boot.first_stage_delay]: [1]
[ro.boot.hardware]: [foster_e]
[ro.boot.modem]: [none]
[ro.boot.orientation]: [0]
[ro.boot.secureos]: [tlk]
[ro.boot.security]: [enabled]
[ro.boot.serialno]: [[NON PUBLIÉ]]
[ro.boot.system_root_blkdev]: [/dev/mmcblk0p19]
[ro.boot.touch_feature]: [0]
[ro.boot.touch_panel_id]: [63]
[ro.boot.touch_vendor_id]: [0]
[ro.bootimage.build.date.utc]: [1769836803]
[ro.bootimage.build.date]: [Fri Jan 30 21:20:03 PST 2026]
[ro.bootimage.build.fingerprint]: [NVIDIA/foster_e/foster:11/RQ1A.210105.003/13961456_4387.0822:user/release-keys]
[ro.bootloader]: [32.00.2019.50-t210-c5cc57a8]
[ro.bootmode]: [unknown]
[ro.build.characteristics]: [tv]
[ro.build.date.utc]: [1769836803]
[ro.build.date]: [Fri Jan 30 21:20:03 PST 2026]
[ro.build.description]: [foster_e-user 11 RQ1A.210105.003 13961456_4387.0822 release-keys]
[ro.build.display.id]: [RQ1A.210105.003.13961456_4387.0822]
[ro.build.fingerprint]: [NVIDIA/foster_e/foster:11/RQ1A.210105.003/13961456_4387.0822:user/release-keys]
[ro.build.flavor]: [foster_e-user]
[ro.build.host]: [20ee6491-bb7f-47f8-976d-b7f02203befa-4hiw-2jr46]
[ro.build.id]: [RQ1A.210105.003]
[ro.build.product]: [foster]
[ro.build.system_root_image]: [true]
[ro.build.tags]: [release-keys]
[ro.build.type]: [user]
[ro.build.user]: [NVIDIA]
[ro.build.version.all_codenames]: [REL]
[ro.build.version.base_os]: []
[ro.build.version.codename]: [REL]
[ro.build.version.incremental]: [13961456_4387.0822]
[ro.build.version.min_supported_target_sdk]: [23]
[ro.build.version.ota]: [9.2.4(33.2.0.400)]
[ro.build.version.preview_sdk]: [0]
[ro.build.version.preview_sdk_fingerprint]: [REL]
[ro.build.version.release]: [11]
[ro.build.version.release_or_codename]: [11]
[ro.build.version.sdk]: [30]
[ro.build.version.security_patch]: [2026-01-05]
[ro.com.google.clientidbase]: [android-nvidia]
[ro.com.google.gmsversion]: [R_GTVS_sample]
[ro.config.media_vol_default]: [15]
[ro.config.size_override]: [1920,1080]
[ro.control_privapp_permissions]: [enforce]
[ro.crypto.state]: [unencrypted]
[ro.dalvik.vm.native.bridge]: [0]
[ro.debuggable]: [0]
[ro.frp.pst]: [/dev/block/platform/sdhci-tegra.3/by-name/RP2]
[ro.hardware.gatekeeper]: [tlk.tegra]
[ro.hardware]: [foster_e]
[ro.hdmi.cec.source.playback_device_action_on_routing_control]: [wake_up_and_send_active_source]
[ro.hdmi.device_type]: [4]
[ro.hdmi.wake_on_hotplug]: [0]
[ro.hwui.use_vulkan]: []
[ro.input.noresample]: [1]
[ro.iorapd.enable]: [true]
[ro.kernel.ebpf.supported]: [true]
[ro.logd.size.stats]: [64K]
[ro.minui.pixel_format]: [BGRA_8888]
[ro.odm.build.date.utc]: [1769836803]
[ro.odm.build.date]: [Fri Jan 30 21:20:03 PST 2026]
[ro.odm.build.fingerprint]: [NVIDIA/foster_e/foster:11/RQ1A.210105.003/13961456_4387.0822:user/release-keys]
[ro.odm.build.id]: [RQ1A.210105.003]
[ro.odm.build.tags]: [release-keys]
[ro.odm.build.type]: [user]
[ro.odm.build.version.incremental]: [13961456_4387.0822]
[ro.odm.build.version.release]: [11]
[ro.odm.build.version.release_or_codename]: [11]
[ro.odm.build.version.sdk]: [30]
[ro.odm.product.cpu.abilist32]: [armeabi-v7a,armeabi]
[ro.odm.product.cpu.abilist64]: [arm64-v8a]
[ro.odm.product.cpu.abilist]: [arm64-v8a,armeabi-v7a,armeabi]
[ro.oem.key1]: [ATV00100015]
[ro.opengles.version]: [196610]
[ro.postinstall.fstab.prefix]: [/system]
[ro.product.board]: [foster_e]
[ro.product.brand]: [NVIDIA]
[ro.product.build.date.utc]: [1769836803]
[ro.product.build.date]: [Fri Jan 30 21:20:03 PST 2026]
[ro.product.build.fingerprint]: [NVIDIA/foster_e/foster:11/RQ1A.210105.003/13961456_4387.0822:user/release-keys]
[ro.product.build.id]: [RQ1A.210105.003]
[ro.product.build.tags]: [release-keys]
[ro.product.build.type]: [user]
[ro.product.build.version.incremental]: [13961456_4387.0822]
[ro.product.build.version.release]: [11]
[ro.product.build.version.release_or_codename]: [11]
[ro.product.build.version.sdk]: [30]
[ro.product.cpu.abi]: [arm64-v8a]
[ro.product.cpu.abilist32]: [armeabi-v7a,armeabi]
[ro.product.cpu.abilist64]: [arm64-v8a]
[ro.product.cpu.abilist]: [arm64-v8a,armeabi-v7a,armeabi]
[ro.product.device]: [foster]
[ro.product.first_api_level]: [21]
[ro.product.locale]: [en-US]
[ro.product.manufacturer]: [NVIDIA]
[ro.product.model]: [SHIELD Android TV]
[ro.product.name]: [foster_e]
[ro.product.odm.brand]: [NVIDIA]
[ro.product.odm.device]: [foster]
[ro.product.odm.manufacturer]: [NVIDIA]
[ro.product.odm.model]: [SHIELD Android TV]
[ro.product.odm.name]: [foster_e]
[ro.product.product.brand]: [NVIDIA]
[ro.product.product.device]: [foster]
[ro.product.product.manufacturer]: [NVIDIA]
[ro.product.product.model]: [SHIELD Android TV]
[ro.product.product.name]: [foster_e]
[ro.product.system.brand]: [NVIDIA]
[ro.product.system.device]: [foster]
[ro.product.system.manufacturer]: [NVIDIA]
[ro.product.system.model]: [SHIELD Android TV]
[ro.product.system.name]: [foster_e]
[ro.product.system_ext.brand]: [NVIDIA]
[ro.product.system_ext.device]: [foster]
[ro.product.system_ext.manufacturer]: [NVIDIA]
[ro.product.system_ext.model]: [SHIELD Android TV]
[ro.product.system_ext.name]: [foster_e]
[ro.product.vendor.brand]: [NVIDIA]
[ro.product.vendor.device]: [foster]
[ro.product.vendor.manufacturer]: [NVIDIA]
[ro.product.vendor.model]: [SHIELD Android TV]
[ro.product.vendor.name]: [foster_e]
[ro.property_service.version]: [2]
[ro.radio.noril]: [true]
[ro.revision]: [0]
[ro.secure]: [1]
[ro.serialno]: [[NON PUBLIÉ]]
[ro.sf.lcd_density]: [320]
[ro.surface_flinger.vsync_event_phase_offset_ns]: [100000]
[ro.surface_flinger.vsync_sf_event_phase_offset_ns]: [100000]
[ro.system.build.date.utc]: [1769836803]
[ro.system.build.date]: [Fri Jan 30 21:20:03 PST 2026]
[ro.system.build.fingerprint]: [NVIDIA/foster_e/foster:11/RQ1A.210105.003/13961456_4387.0822:user/release-keys]
[ro.system.build.id]: [RQ1A.210105.003]
[ro.system.build.tags]: [release-keys]
[ro.system.build.type]: [user]
[ro.system.build.version.incremental]: [13961456_4387.0822]
[ro.system.build.version.release]: [11]
[ro.system.build.version.release_or_codename]: [11]
[ro.system.build.version.sdk]: [30]
[ro.system_ext.build.date.utc]: [1769836803]
[ro.system_ext.build.date]: [Fri Jan 30 21:20:03 PST 2026]
[ro.system_ext.build.fingerprint]: [NVIDIA/foster_e/foster:11/RQ1A.210105.003/13961456_4387.0822:user/release-keys]
[ro.system_ext.build.id]: [RQ1A.210105.003]
[ro.system_ext.build.tags]: [release-keys]
[ro.system_ext.build.type]: [user]
[ro.system_ext.build.version.incremental]: [13961456_4387.0822]
[ro.system_ext.build.version.release]: [11]
[ro.system_ext.build.version.release_or_codename]: [11]
[ro.system_ext.build.version.sdk]: [30]
[ro.treble.enabled]: [true]
[ro.vendor.build.date.utc]: [1769836803]
[ro.vendor.build.date]: [Fri Jan 30 21:20:03 PST 2026]
[ro.vendor.build.fingerprint]: [NVIDIA/foster_e/foster:11/RQ1A.210105.003/13961456_4387.0822:user/release-keys]
[ro.vendor.build.id]: [RQ1A.210105.003]
[ro.vendor.build.security_patch]: []
[ro.vendor.build.tags]: [release-keys]
[ro.vendor.build.type]: [user]
[ro.vendor.build.version.incremental]: [13961456_4387.0822]
[ro.vendor.build.version.release]: [11]
[ro.vendor.build.version.release_or_codename]: [11]
[ro.vendor.build.version.sdk]: [30]
[ro.vendor.nv.usb.pid.accessory.adb]: [7105]
[ro.vendor.nv.usb.pid.adb]: [7104]
[ro.vendor.nv.usb.pid.audio_source.adb]: [7106]
[ro.vendor.nv.usb.pid.ecm.adb]: [710c]
[ro.vendor.nv.usb.pid.ecm]: [710b]
[ro.vendor.nv.usb.pid.midi.adb]: [710A]
[ro.vendor.nv.usb.pid.midi]: [7109]
[ro.vendor.nv.usb.pid.mtp.adb]: [B431]
[ro.vendor.nv.usb.pid.mtp]: [B430]
[ro.vendor.nv.usb.pid.ncm.adb]: [7108]
[ro.vendor.nv.usb.pid.ncm]: [7107]
[ro.vendor.nv.usb.pid.ptp.adb]: [B433]
[ro.vendor.nv.usb.pid.ptp]: [B432]
[ro.vendor.nv.usb.pid.rndis.acm.adb]: [af00]
[ro.vendor.nv.usb.pid.rndis.adb]: [B435]
[ro.vendor.nv.usb.pid.rndis]: [B434]
[ro.vendor.nv.usb.vid]: [0955]
[ro.vendor.product.config.shipped_with_full_treble]: [false]
[ro.vendor.product.cpu.abilist32]: [armeabi-v7a,armeabi]
[ro.vendor.product.cpu.abilist64]: [arm64-v8a]
[ro.vendor.product.cpu.abilist]: [arm64-v8a,armeabi-v7a,armeabi]
[ro.vendor.security]: [enabled]
[ro.vendor.sf.override_lcd_density]: [1]
[ro.vendor.sf.vrr.enabled]: [1]
[ro.vendor.surface_flinger.use_frame_rate_api]: [false]
[ro.vendor.tegra.0x3e9c5c]: [16777216]
[ro.vendor.tegra.0x523dc5]: [0x3f000000]
[ro.vendor.tegra.58027529]: [0x00000002]
[ro.vendor.tegra.AF73C63E]: [0x80007ffd]
[ro.vendor.tegra.OGL_VidHeapReuseRatio]: [5]
[ro.vendor.tegra.a3456abe]: [0x087F6080]
[ro.vendor.tegra.deepisp.cpu_max]: [-1]
[ro.vendor.tegra.deepisp.cpu_min]: [-1]
[ro.vendor.tegra.deepisp.cpu_pri]: [15]
[ro.vendor.tegra.deepisp.emc_min]: [106560]
[ro.vendor.tegra.deepisp.gpu_max]: [-1]
[ro.vendor.tegra.deepisp.gpu_min]: [1075200]
[ro.vendor.tegra.deepisp.gpu_pri]: [15]
[ro.vendor.tegra.glc.cpu_max]: [-1]
[ro.vendor.tegra.glc.cpu_min]: [-1]
[ro.vendor.tegra.glc.cpu_pri]: [15]
[ro.vendor.tegra.glc.emc_min]: [4080]
[ro.vendor.tegra.glc.gpu_max]: [-1]
[ro.vendor.tegra.glc.gpu_min]: [614400]
[ro.vendor.tegra.glc.gpu_pri]: [15]
[ro.vendor.tegra.yuv.cpu_max]: [-1]
[ro.vendor.tegra.yuv.cpu_min]: [-1]
[ro.vendor.tegra.yuv.cpu_pri]: [15]
[ro.vendor.tegra.yuv.emc_min]: [106560]
[ro.vendor.tegra.yuv.gpu_max]: [-1]
[ro.vendor.tegra.yuv.gpu_min]: [921600]
[ro.vendor.tegra.yuv.gpu_pri]: [15]
[ro.vendor.wifi.sdmmc]: [1]
[ro.vndk.version]: [30]
[ro.wifi.channels]: []
[ro.zygote]: [zygote64_32]
[security.perf_harden]: [1]
[selinux.restorecon_recursive]: [/mnt/expand/b6790f0b-9d50-4c81-ba35-16de7a9d9549]
[service.adb.tcp.port]: [5555]
[service.bootanim.exit]: [1]
[service.sf.present_timestamp]: [1]
[sys.boot.reason.last]: [reboot,nvpowermenurestart]
[sys.boot.reason]: [reboot,nvpowarmenurestart]
[sys.boot_completed]: [1]
[sys.bootstat.first_boot_completed]: [1]
[sys.device.sku]: [E12571]
[sys.isolated_storage_snapshot]: [true]
[sys.oem_unlock_allowed]: [1]
[sys.rescue_boot_count]: [1]
[sys.retaildemo.enabled]: [0]
[sys.sysctl.extra_free_kbytes]: [24300]
[sys.sysctl.tcp_def_init_rwnd]: [60]
[sys.system_server.start_count]: [1]
[sys.system_server.start_elapsed]: [17193]
[sys.system_server.start_uptime]: [17193]
[sys.usb.config]: [mtp]
[sys.usb.configfs]: [1]
[sys.usb.controller]: [700d0000.xudc]
[sys.usb.ffs.ready]: [1]
[sys.usb.state]: [mtp]
[sys.use_memfd]: [false]
[sys.user.0.ce_available]: [true]
[sys.wal.normal]: [tz4,PGDatabase,launcher.db,playlog.db,music.db]
[sys.wal.off]: [external.db,iu.upload.db]
[sys.wifitracing.started]: [1]
[vendor.debug.enable_console_logs]: [false]
[vendor.display-size]: [3840x2160]
[vendor.sys.display-colorspace]: [YUV420-8]
[vendor.sys.hdmi.primary]: [1]
[vendor.sys.hwc.hdr.supported]: [0]
[vendor.sys.tegra.dpy.rotation]: [0]
[vendor.sys.tegra.refresh]: [59.940]
[vendor.sys.ui.resolution.x]: [1920]
[vendor.sys.ui.resolution.y]: [1080]
[vendor.tegra.0x523dd1]: [2]
[vendor.tegra.display-size]: [3840x2160]
[vold.has_adoptable]: [1]
[vold.has_quota]: [1]
[vold.has_reserved]: [0]
[vold.post_fs_data_done]: [1]
[wifi.direct.interface]: [p2p-dev-wlan0]
[wifi.interface]: [wlan0]
reboot,nvpowarmenurestart,1791780219]

### mémoire
MemTotal:        3016572 kB
MemFree:          154256 kB
MemAvailable:    1333252 kB
Buffers:           31464 kB
Cached:          1171388 kB
SwapCached:            0 kB
Active:          1658304 kB
Inactive:         605664 kB
Active(anon):     797716 kB
Inactive(anon):   271472 kB
Active(file):     860588 kB
Inactive(file):   334192 kB
Unevictable:           0 kB
Mlocked:               0 kB
SwapTotal:        524284 kB
SwapFree:         524184 kB
Dirty:                64 kB
Writeback:             0 kB
AnonPages:       1061160 kB
Mapped:           802116 kB
Shmem:              8080 kB
Slab:             142648 kB
SReclaimable:      52572 kB
SUnreclaim:        90076 kB
KernelStack:       28256 kB
PageTables:        61052 kB
NFS_Unstable:          0 kB
Bounce:                0 kB
WritebackTmp:          0 kB
CommitLimit:     2032568 kB

### /dev NVIDIA et vidéo
crw-rw-rw- 1 root      root      505,   0 2026-10-25 13:32 nvhost-as-gpu
crw-rw-rw- 1 root      root      241,   0 2026-10-25 13:32 nvhost-ctrl
crw-rw-rw- 1 root      root      505,   2 2026-10-25 13:32 nvhost-ctrl-gpu
crw-rw---- 1 media     camera    241,  26 2026-10-25 13:32 nvhost-ctrl-isp
crw-rw---- 1 media     camera    241,  30 2026-10-25 13:32 nvhost-ctrl-isp.1
crw-rw---- 1 media     drm       241,  14 2026-10-25 13:32 nvhost-ctrl-nvdec
crw-rw---- 1 media     camera    241,  34 2026-10-25 13:32 nvhost-ctrl-vi
crw------- 1 root      root      505,   3 2026-10-25 13:32 nvhost-ctxsw-gpu
crw-rw---- 1 root      root      505,   4 2026-10-25 13:32 nvhost-dbg-gpu
crw-rw-rw- 1 root      root      505,   1 2026-10-25 13:32 nvhost-gpu
crw-rw---- 1 media     camera    241,  25 2026-10-25 13:32 nvhost-isp
crw-rw---- 1 media     camera    241,  29 2026-10-25 13:32 nvhost-isp.1
crw-rw---- 1 media     camera    241,   5 2026-10-25 13:32 nvhost-msenc
crw-rw---- 1 media     camera    241,  13 2026-10-25 13:32 nvhost-nvdec
crw-rw-rw- 1 media     camera    241,   9 2026-10-25 13:32 nvhost-nvjpg
crw------- 1 root      root      506,   0 2026-10-25 13:32 nvhost-power-gpu
crw------- 1 root      root      505,   6 2026-10-25 13:32 nvhost-prof-ctx-gpu
crw------- 1 root      root      505,   7 2026-10-25 13:32 nvhost-prof-dev-gpu
crw-rw---- 1 root      root      505,   5 2026-10-25 13:32 nvhost-prof-gpu
crw------- 1 root      root      505,   8 2026-10-25 13:32 nvhost-sched-gpu
crw-rw-rw- 1 root      root      241,  17 2026-10-25 13:32 nvhost-tsec
crw-rw-rw- 1 root      root      241,  21 2026-10-25 13:32 nvhost-tsecb
crw-rw-rw- 1 root      root      505,   9 2026-10-25 13:32 nvhost-tsg-gpu
crw-rw---- 1 media     camera    241,  33 2026-10-25 13:32 nvhost-vi
crw-rw-rw- 1 root      root      241,   1 2026-10-25 13:32 nvhost-vic

### bibliothèques publiques (nvmedia)

### dmesg (bonus)

### lsusb (bonus)
Bus 002 Device 004: ID 174c:5136
Bus 002 Device 003: ID 0bda:8153
Bus 002 Device 002: ID 05e3:0616
Bus 001 Device 001: ID 1d6b:0002
Bus 002 Device 001: ID 1d6b:0003
Bus 001 Device 002: ID 05e3:0610

```

### g0-dlopen-shell.txt

```text
libnvmedia.so      ÉCHEC (dlopen failed: library "libnvmedia.so" not found)
libnvmpeg11.so     ÉCHEC (dlopen failed: library "libnvmpeg11.so" not found)
libnvhost.so       ÉCHEC (dlopen failed: library "libnvhost.so" not found)

```

### g0-app-namespace.txt

```text
ÉCHEC	libnvmedia.so
ÉCHEC	libnvmpeg11.so
ÉCHEC	libnvhost.so
OK	nvtranscode.so
OK	libnvos.so
OK	libnvgr.so
OK	libnvcpl_vendor.so
OK	libnvsi_ll_2.so
OK	libndkbinderutil.so
OK	libtsec_wrapper.so
```

### g1-storage.txt

```text

### volume USB
AX58U
emulated
self

### RW
/storage/AX58U : R/O (attendu)

### lecture ffprobe
média : /storage/AX58U/TOSHIBA_EXT/DSC_0154.mkv
[STREAM]
codec_name=h264
width=1920
height=1080
[/STREAM]
[STREAM]
codec_name=flac
sample_rate=48000
[/STREAM]
[FORMAT]
filename=/storage/AX58U/TOSHIBA_EXT/DSC_0154.mkv
nb_streams=2
nb_programs=0
nb_stream_groups=0
format_name=matroska,webm
format_long_name=Matroska / WebM
start_time=0.000000
duration=108.212000
size=199721106
bit_rate=14765172
probe_score=100
TAG:title=DSC_0154
TAG:encoder=libebml v1.3.4 + libmatroska v1.4.5
TAG:creation_time=2016-09-17T22:27:25.000000Z
[/FORMAT]

```

### g2-bench.tsv

```text
variant	input	route	fps	speed
sw	in_1080_h264.mp4	dcpu-enc-cpu	62	2.06
sw	in_4k_h264.mp4	dcpu-4k-scale	23	0.751
v4l2m2m	in_1080_h264.mp4	dcpu-enc-m2m	0	0
v4l2m2m	in_4k_h264.mp4	dcpu-4k-scale	0	0
v4l2m2m	in_1080_hevc.mp4	hwdec-enc	0	0
mediacodec	in_1080_h264.mp4	dcpu-enc-mc	116	3.82
mediacodec	in_4k_h264.mp4	dcpu-4k-scale	26	0.857
mediacodec	in_1080_hevc.mp4	hwdec-hwenc	108	3.55

```

## Verdicts par marche (constats mesurés le 2026-10-25, à valider par le propriétaire : portes G0/G1/G2)

| Marche | Verdict | FPS mesurés | Observations |
|---|---|---|---|
| V4L2-M2M | **no-go** | — (0 sur toutes les routes) | Aucun nœud `/dev/video*` sur foster_e Android 11 ; `h264_v4l2m2m` échoue sur l'appareil (« Could not open encoder », code -22) — confirmé en 2 routes |
| MediaCodec | **GO** | 1080p (décodage CPU → encodage HW H.264) : **116 fps / 3,82×** ; HEVC décodage HW + H.264 encodage HW (100 % matériel) : **108 fps / 3,55×** ; 4K H.264 décode CPU + scale + encodage HW : 26 fps / 0,86× (sous temps réel, comparable à Plex sur Shield) | Encodeur `h264_mediacodec` **présent** (`CONFIG_H264_MEDIACODEC_ENCODER=1` dans `config_components.h`, un membre dans `config.h`) — nécessite `--enable-mediacodec --enable-jni` + activation explicite des codecs ; encodeurs exposés par le binaire sur appareil : av1/hevc/h264/mpeg4/vp8/vp9_mediacodec. NB : 4K HEVC (décodage HW 4K) non mesurée ici faute d'input HEVC 4K — complément possible au G3. NB : la baseline CPU du TSV est l'encodeur natif mpeg4 (libx264 absent de la sonde) — plus léger que x264, à interpréter comme plancher. |
| NVMedia décodage | atteint via MediaCodec (ci-dessus) | — | `libnvmpeg11*` absentes de Shield Android (c'est le domaine Jetson/L4T) ; le décodage HW passe par le HAL MediaCodec (nvhost-nvdec, groupe media) — la route GO couvre ce besoin |
| NVMedia encodage (option G5) | piste **favorable**, chantier séparé | — | `/vendor/etc/public.libraries.txt` liste les libs NVIDIA accessibles depuis une app (`nvtranscode.so`, `libnvos.so`, `libnvgr.so`, `libnvcpl_vendor.so`, `libnvsi_ll_2.so`, `libndkbinderutil.so`, `libtsec_wrapper.so`) — **toutes dlopen OK dans le namespace applicatif** (sonde APK, constat authentifié) ; le chemin type Plex (encode NVMedia via nvtranscode) est donc accessible sans root |

### Synthèse pour les portes

- **Porte G0 : PASS** — Shield foster_e, Android 11 (RQ1A.210105.003, firmware 33.2.0.400, patch sécurité 2026-01-05), RAM 3 016 Mo (mieux que les 2 Go modélisés), arm64-v8a ; ADB réseau actif et persistant ; les libs vendor whitelisted sont atteignables depuis l'espace app.
- **Porte G1 : PASS** — volume `/storage/AX58U` (partage réseau du routeur ASUS monté par Android) lisible en RO attendue ; ffprobe lit un média réel H.264/FLAC matroska ; l'écriture sera donc confinée à l'app (spec §2 inchangée : métadonnées dans `filesDir`).
- **Porte G2 : GO sur MediaCodec** — transcodage 100 % matériel : décode HEVC (HW) → encode H.264 (HW) à 3,55× temps réel ; H.264→H.264 à 3,82×. V4L2-M2M = no-go démontré. NVMedia = voie d'exploration G5 (libs whitelisted).

Rapport consolidé généré par tools/probe/report.sh, complété avec les mesures du 2026-10-25 ; régénération par report.sh refusée (garde).
