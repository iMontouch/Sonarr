﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Mono.Unix;
using Mono.Unix.Native;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Test.DiskTests;
using NzbDrone.Mono.Disk;

namespace NzbDrone.Mono.Test.DiskProviderTests
{
    [TestFixture]
    [Platform("Mono")]
    public class DiskProviderFixture : DiskProviderFixtureBase<DiskProvider>
    {
        public DiskProviderFixture()
        {
            MonoOnly();
        }

        protected override void SetWritePermissions(string path, bool writable)
        {
            if (Environment.UserName == "root")
            {
                Assert.Inconclusive("Need non-root user to test write permissions.");
            }

            // Remove Write permissions, we're still owner so we can clean it up, but we'll have to do that explicitly.

            var entry = UnixFileSystemInfo.GetFileSystemEntry(path);

            if (writable)
            {
                entry.FileAccessPermissions |= FileAccessPermissions.UserWrite | FileAccessPermissions.GroupWrite | FileAccessPermissions.OtherWrite;
            }
            else
            {
                entry.FileAccessPermissions &= ~(FileAccessPermissions.UserWrite | FileAccessPermissions.GroupWrite | FileAccessPermissions.OtherWrite);
            }
        }

        [Test]
        public void should_move_symlink()
        {
            var tempFolder = GetTempFilePath();
            Directory.CreateDirectory(tempFolder);

            var file = Path.Combine(tempFolder, "target.txt");
            var source = Path.Combine(tempFolder, "symlink_source.txt");
            var destination = Path.Combine(tempFolder, "symlink_destination.txt");

            File.WriteAllText(file, "Some content");

            new UnixSymbolicLinkInfo(source).CreateSymbolicLinkTo(file);

            Subject.MoveFile(source, destination);

            File.Exists(file).Should().BeTrue();
            File.Exists(source).Should().BeFalse();
            File.Exists(destination).Should().BeTrue();
            UnixFileSystemInfo.GetFileSystemEntry(destination).IsSymbolicLink.Should().BeTrue();

            File.ReadAllText(destination).Should().Be("Some content");
        }

        [Test]
        public void should_copy_symlink()
        {
            var tempFolder = GetTempFilePath();
            Directory.CreateDirectory(tempFolder);

            var file = Path.Combine(tempFolder, "target.txt");
            var source = Path.Combine(tempFolder, "symlink_source.txt");
            var destination = Path.Combine(tempFolder, "symlink_destination.txt");

            File.WriteAllText(file, "Some content");

            new UnixSymbolicLinkInfo(source).CreateSymbolicLinkTo(file);

            Subject.CopyFile(source, destination);

            File.Exists(file).Should().BeTrue();
            File.Exists(source).Should().BeTrue();
            File.Exists(destination).Should().BeTrue();
            UnixFileSystemInfo.GetFileSystemEntry(source).IsSymbolicLink.Should().BeTrue();
            UnixFileSystemInfo.GetFileSystemEntry(destination).IsSymbolicLink.Should().BeTrue();

            File.ReadAllText(source).Should().Be("Some content");
            File.ReadAllText(destination).Should().Be("Some content");
        }

        private void GivenSpecialMount(string rootDir)
        {
            Mocker.GetMock<ISymbolicLinkResolver>()
                .Setup(v => v.GetCompleteRealPath(It.IsAny<string>()))
                .Returns<string>(s => s);

            Mocker.GetMock<IProcMountProvider>()
                .Setup(v => v.GetMounts())
                .Returns(new List<IMount> {
                    new ProcMount(DriveType.Fixed, rootDir, rootDir, "myfs", new MountOptions(new Dictionary<string, string>()))
                });
        }

        [TestCase("/snap/blaat")]
        [TestCase("/var/lib/docker/zfs-storage-mount")]
        public void should_ignore_special_mounts(string rootDir)
        {
            GivenSpecialMount(rootDir);

            var mounts = Subject.GetMounts();

            mounts.Select(d => d.RootDirectory).Should().NotContain(rootDir);
        }

        [TestCase("/snap/blaat")]
        [TestCase("/var/lib/docker/zfs-storage-mount")]
        public void should_return_special_mount_when_queried(string rootDir)
        {
            GivenSpecialMount(rootDir);

            var mount = Subject.GetMount(Path.Combine(rootDir, "dir/somefile.mkv"));

            mount.Should().NotBeNull();
            mount.RootDirectory.Should().Be(rootDir);
        }

        [Test]
        public void should_copy_folder_permissions()
        {
            var src = GetTempFilePath();
            var dst = GetTempFilePath();

            Directory.CreateDirectory(src);

            // Toggle one of the permission flags
            Syscall.stat(src, out var origStat);
            Syscall.chmod(src, origStat.st_mode ^ FilePermissions.S_IWGRP);

            // Verify test setup
            Syscall.stat(src, out var srcStat);
            srcStat.st_mode.Should().NotBe(origStat.st_mode);

            Subject.CreateFolder(dst);

            // Verify test setup
            Syscall.stat(dst, out var dstStat);
            dstStat.st_mode.Should().Be(origStat.st_mode);

            Subject.CopyPermissions(src, dst);

            // Verify CopyPermissions
            Syscall.stat(dst, out dstStat);
            dstStat.st_mode.Should().Be(srcStat.st_mode);
        }

        [Test]
        public void should_set_file_permissions()
        {
            var tempFile = GetTempFilePath();

            File.WriteAllText(tempFile, "File1");
            SetWritePermissions(tempFile, false);

            // Verify test setup
            Syscall.stat(tempFile, out var fileStat);
            NativeConvert.ToOctalPermissionString(fileStat.st_mode).Should().Be("0444");

            Subject.SetPermissions(tempFile, "755", null);
            Syscall.stat(tempFile, out fileStat);
            NativeConvert.ToOctalPermissionString(fileStat.st_mode).Should().Be("0644");

            Subject.SetPermissions(tempFile, "0755", null);
            Syscall.stat(tempFile, out fileStat);
            NativeConvert.ToOctalPermissionString(fileStat.st_mode).Should().Be("0644");

            Subject.SetPermissions(tempFile, "1775", null);
            Syscall.stat(tempFile, out fileStat);
            NativeConvert.ToOctalPermissionString(fileStat.st_mode).Should().Be("1664");
        }

        [Test]
        public void should_set_folder_permissions()
        {
            var tempPath = GetTempFilePath();

            Directory.CreateDirectory(tempPath);
            SetWritePermissions(tempPath, false);

            // Verify test setup
            Syscall.stat(tempPath, out var fileStat);
            NativeConvert.ToOctalPermissionString(fileStat.st_mode).Should().Be("0555");

            Subject.SetPermissions(tempPath, "755", null);
            Syscall.stat(tempPath, out fileStat);
            NativeConvert.ToOctalPermissionString(fileStat.st_mode).Should().Be("0755");

            Subject.SetPermissions(tempPath, "0755", null);
            Syscall.stat(tempPath, out fileStat);
            NativeConvert.ToOctalPermissionString(fileStat.st_mode).Should().Be("0755");

            Subject.SetPermissions(tempPath, "1775", null);
            Syscall.stat(tempPath, out fileStat);
            NativeConvert.ToOctalPermissionString(fileStat.st_mode).Should().Be("1775");

            Subject.SetPermissions(tempPath, "775", null);
            Syscall.stat(tempPath, out fileStat);
            NativeConvert.ToOctalPermissionString(fileStat.st_mode).Should().Be("0775");

            Subject.SetPermissions(tempPath, "750", null);
            Syscall.stat(tempPath, out fileStat);
            NativeConvert.ToOctalPermissionString(fileStat.st_mode).Should().Be("0750");

            Subject.SetPermissions(tempPath, "0051", null);
            Syscall.stat(tempPath, out fileStat);
            NativeConvert.ToOctalPermissionString(fileStat.st_mode).Should().Be("0051");
        }

        [Test]
        public void IsValidFolderPermissionMask_should_return_correct()
        {
            // No special bits should be set
            Subject.IsValidFolderPermissionMask("1755").Should().BeFalse();
            Subject.IsValidFolderPermissionMask("2755").Should().BeFalse();
            Subject.IsValidFolderPermissionMask("4755").Should().BeFalse();
            Subject.IsValidFolderPermissionMask("7755").Should().BeFalse();

            // Folder should be readable and writeable by owner
            Subject.IsValidFolderPermissionMask("0000").Should().BeFalse();
            Subject.IsValidFolderPermissionMask("0100").Should().BeFalse();
            Subject.IsValidFolderPermissionMask("0200").Should().BeFalse();
            Subject.IsValidFolderPermissionMask("0300").Should().BeFalse();
            Subject.IsValidFolderPermissionMask("0400").Should().BeFalse();
            Subject.IsValidFolderPermissionMask("0500").Should().BeFalse();
            Subject.IsValidFolderPermissionMask("0600").Should().BeFalse();
            Subject.IsValidFolderPermissionMask("0700").Should().BeTrue();
        }
    }
}
