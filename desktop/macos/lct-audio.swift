// lct-audio: captures audio for 课堂同传助手 and writes it to standard output as raw
// 16 kHz mono signed 16-bit little-endian PCM (what the speech recognizer wants).
//
//   lct-audio --system        sound played by the computer (ScreenCaptureKit; needs the Screen Recording permission)
//   lct-audio --mic           the default microphone
//   lct-audio --list-inputs   names of the microphones, one per line
//
// Problems go to standard error (in Chinese, shown to the user) together with a non-zero exit code.
// Build: swiftc -O -swift-version 5 -target arm64-apple-macos13 lct-audio.swift -o lct-audio   (or x86_64-apple-macos13)

import AVFoundation
import CoreGraphics
import CoreMedia
import Darwin
import Foundation
import ScreenCaptureKit

func fail(_ message: String) -> Never {
    FileHandle.standardError.write((message + "\n").data(using: .utf8)!)
    exit(1)
}

let targetFormat = AVAudioFormat(commonFormat: .pcmFormatInt16, sampleRate: 16000, channels: 1, interleaved: true)!

// Converts whatever the system delivers to the target format and writes it out.
final class PcmWriter {
    private var converter: AVAudioConverter?
    private var inputFormat: AVAudioFormat?

    func write(_ buffer: AVAudioPCMBuffer) {
        if converter == nil || inputFormat != buffer.format {
            inputFormat = buffer.format
            converter = AVAudioConverter(from: buffer.format, to: targetFormat)
        }
        guard let converter = converter, buffer.frameLength > 0 else { return }

        let ratio = targetFormat.sampleRate / buffer.format.sampleRate
        let capacity = AVAudioFrameCount(Double(buffer.frameLength) * ratio) + 64
        guard let output = AVAudioPCMBuffer(pcmFormat: targetFormat, frameCapacity: capacity) else { return }

        var supplied = false
        var error: NSError?
        let status = converter.convert(to: output, error: &error) { _, inputStatus in
            if supplied {
                inputStatus.pointee = .noDataNow
                return nil
            }
            supplied = true
            inputStatus.pointee = .haveData
            return buffer
        }
        if status == .error || output.frameLength == 0 { return }

        let count = Int(output.frameLength) * 2
        guard let samples = output.int16ChannelData?[0] else { return }
        var offset = 0
        samples.withMemoryRebound(to: UInt8.self, capacity: count) { bytes in
            while offset < count {
                let written = Darwin.write(1, bytes + offset, count - offset)
                // The program that started us has gone away.
                if written <= 0 { exit(0) }
                offset += written
            }
        }
    }
}

// The sound of the whole computer, except this program.
final class SystemAudio: NSObject, SCStreamOutput, SCStreamDelegate {
    private let writer = PcmWriter()
    private let queue = DispatchQueue(label: "lct-audio.system")
    private var stream: SCStream?

    func start() async throws {
        if !CGPreflightScreenCaptureAccess() {
            _ = CGRequestScreenCaptureAccess()
        }
        let content: SCShareableContent
        do {
            content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
        } catch {
            fail("没有“屏幕与系统音频录制”权限。请到 系统设置 → 隐私与安全性 → 屏幕与系统音频录制，允许“课堂同传助手”，然后重新打开本程序。")
        }
        guard let display = content.displays.first else {
            fail("找不到可用的显示器，无法采集系统声音。")
        }

        let filter = SCContentFilter(display: display, excludingWindows: [])
        let configuration = SCStreamConfiguration()
        configuration.capturesAudio = true
        configuration.excludesCurrentProcessAudio = true
        configuration.sampleRate = 16000
        configuration.channelCount = 1
        // Only the sound is wanted: ask for the smallest, slowest picture.
        configuration.width = 2
        configuration.height = 2
        configuration.minimumFrameInterval = CMTime(value: 1, timescale: 2)

        let stream = SCStream(filter: filter, configuration: configuration, delegate: self)
        try stream.addStreamOutput(self, type: .screen, sampleHandlerQueue: queue)
        try stream.addStreamOutput(self, type: .audio, sampleHandlerQueue: queue)
        try await stream.startCapture()
        self.stream = stream
    }

    func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer, of type: SCStreamOutputType) {
        // Video frames are ignored.
        guard type == .audio, sampleBuffer.isValid, let buffer = pcmBuffer(from: sampleBuffer) else { return }
        writer.write(buffer)
    }

    func stream(_ stream: SCStream, didStopWithError error: Error) {
        fail("系统声音采集中断：\(error.localizedDescription)")
    }

    private func pcmBuffer(from sampleBuffer: CMSampleBuffer) -> AVAudioPCMBuffer? {
        guard let description = CMSampleBufferGetFormatDescription(sampleBuffer),
              let basic = CMAudioFormatDescriptionGetStreamBasicDescription(description) else { return nil }
        var streamDescription = basic.pointee
        guard let format = AVAudioFormat(streamDescription: &streamDescription) else { return nil }
        let frames = AVAudioFrameCount(CMSampleBufferGetNumSamples(sampleBuffer))
        guard frames > 0, let buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: frames) else { return nil }
        buffer.frameLength = frames
        let status = CMSampleBufferCopyPCMDataIntoAudioBufferList(
            sampleBuffer, at: 0, frameCount: Int32(frames), into: buffer.mutableAudioBufferList)
        return status == noErr ? buffer : nil
    }
}

// The default microphone.
final class Microphone {
    private let writer = PcmWriter()
    private let engine = AVAudioEngine()

    func start() async throws {
        let allowed = await AVCaptureDevice.requestAccess(for: .audio)
        if !allowed {
            fail("没有麦克风权限。请到 系统设置 → 隐私与安全性 → 麦克风，允许“课堂同传助手”，然后重新打开本程序。")
        }
        let input = engine.inputNode
        let format = input.outputFormat(forBus: 0)
        if format.sampleRate == 0 || format.channelCount == 0 {
            fail("找不到可用的麦克风。")
        }
        input.installTap(onBus: 0, bufferSize: 4096, format: format) { [writer] buffer, _ in
            writer.write(buffer)
        }
        engine.prepare()
        try engine.start()
    }
}

func listInputs() {
    let discovery = AVCaptureDevice.DiscoverySession(
        deviceTypes: [.builtInMicrophone, .externalUnknown], mediaType: .audio, position: .unspecified)
    for device in discovery.devices {
        print(device.localizedName)
    }
}

let arguments = CommandLine.arguments
guard arguments.count >= 2 else {
    fail("用法：lct-audio --system | --mic | --list-inputs")
}

switch arguments[1] {
case "--list-inputs":
    listInputs()
    exit(0)
case "--system":
    let system = SystemAudio()
    Task {
        do {
            try await system.start()
        } catch {
            fail("无法采集系统声音：\(error.localizedDescription)")
        }
    }
    dispatchMain()
case "--mic":
    let microphone = Microphone()
    Task {
        do {
            try await microphone.start()
        } catch {
            fail("无法打开麦克风：\(error.localizedDescription)")
        }
    }
    dispatchMain()
default:
    fail("用法：lct-audio --system | --mic | --list-inputs")
}
